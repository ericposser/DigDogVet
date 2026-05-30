using DigDog.Data;
using DigDog.Models;
using DigDog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DigDog.Controllers;

[Authorize(Roles = "Admin")]
public class UsuarioController : UtilController
{
    private readonly RoleManager<IdentityRole> _gerenciadorRole;
    private readonly PermissaoService _permissaoService;
    private readonly UserManager<IdentityUser> _gerenciadorUsuarioLocal;

    public UsuarioController(
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao,
        Contexto contexto,
        RoleManager<IdentityRole> gerenciadorRole,
        PermissaoService permissaoService)
        : base(gerenciadorUsuario, provedorProtecao, contexto)
    {
        _gerenciadorRole         = gerenciadorRole;
        _permissaoService        = permissaoService;
        _gerenciadorUsuarioLocal = gerenciadorUsuario;
    }

    public async Task<IActionResult> Index()
    {
        var idUsuarioAdmin = ObterIdUsuario();
        var idEmpresa      = await ObterIdEmpresaAsync();

        // Busca os IDs dos funcionários da empresa (excluindo o Admin logado)
        var idsVinculos = await _contextoBase.EmpresaUsuario
            .Where(eu => eu.IdEmpresa == idEmpresa && eu.IdUsuario != idUsuarioAdmin)
            .Select(eu => eu.IdUsuario)
            .ToListAsync();

        if (!idsVinculos.Any())
            return View(new List<UsuarioRoleViewModel>());

        // ── Carrega tudo em lotes — sem N+1 ───────────────────────────────

        // 1. Todos os usuários de uma vez via tabela do Identity
        var usuarios = await _contextoBase.Users
            .Where(u => idsVinculos.Contains(u.Id))
            .ToListAsync();

        // 2. Todas as roles de uma vez (AspNetUserRoles JOIN AspNetRoles)
        var rolesDoUsuarios = new Dictionary<string, string?>(); // idUsuario → nomeRole
        foreach (var usuario in usuarios)
        {
            var roles = await _gerenciadorUsuarioLocal.GetRolesAsync(usuario);
            rolesDoUsuarios[usuario.Id] = roles.FirstOrDefault();
        }

        // 3. Todas as roles necessárias de uma vez
        var nomesRolesUsadas = rolesDoUsuarios.Values
            .Where(r => r != null).Distinct().ToList();
        var rolesObjs = await _gerenciadorRole.Roles
            .Where(r => nomesRolesUsadas.Contains(r.Name!))
            .ToDictionaryAsync(r => r.Name!, r => r);

        // 4. Todas as claims de uma vez via tabela UserClaims
        var claimsDoUsuarios = await _contextoBase.UserClaims
            .Where(uc => idsVinculos.Contains(uc.UserId) && uc.ClaimType == "NomeFuncionario")
            .ToDictionaryAsync(uc => uc.UserId, uc => uc.ClaimValue);

        // 5. Telefones já estão em AspNetUsers (PhoneNumber) — carregados no passo 1

        // ── Monta a lista ──────────────────────────────────────────────────
        var lista = usuarios.Select(usuario =>
        {
            var nomeRole = rolesDoUsuarios.GetValueOrDefault(usuario.Id);
            var roleObj  = nomeRole != null && rolesObjs.TryGetValue(nomeRole, out var r) ? r : null;

            return new UsuarioRoleViewModel
            {
                IdUsuario = usuario.Id,
                Email     = usuario.Email!,
                Nome      = claimsDoUsuarios.GetValueOrDefault(usuario.Id),
                NomeRole  = nomeRole,
                IdRole    = roleObj?.Id,
                EhAdmin   = nomeRole == "Admin",
                Telefone  = usuario.PhoneNumber
            };
        }).OrderBy(u => u.Email).ToList();

        return View(lista);
    }

    public async Task<IActionResult> Create()
    {
        await CarregarRoles();
        return View(new UsuarioFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UsuarioFormViewModel modelo)
    {
        ValidarTamanhoTexto(nameof(modelo.Nome),            modelo.Nome,            100);
        ValidarTamanhoTexto(nameof(modelo.Email),           modelo.Email,           254);
        ValidarTamanhoTexto(nameof(modelo.Telefone),        modelo.Telefone,        20);
        ValidarTamanhoTexto(nameof(modelo.SenhaTemporaria), modelo.SenhaTemporaria, 128);

        if (!ModelState.IsValid)
        {
            await CarregarRoles(modelo.IdRole);
            return View(modelo);
        }

        var existente = await _gerenciadorUsuarioLocal.FindByEmailAsync(modelo.Email);
        if (existente != null)
        {
            ModelState.AddModelError(nameof(modelo.Email), "Este e-mail já está cadastrado.");
            await CarregarRoles(modelo.IdRole);
            return View(modelo);
        }

        var novoUsuario = new IdentityUser
        {
            UserName       = modelo.Email,
            Email          = modelo.Email,
            EmailConfirmed = true
        };

        var resultado = await _gerenciadorUsuarioLocal.CreateAsync(novoUsuario, modelo.SenhaTemporaria);
        if (!resultado.Succeeded)
        {
            ModelState.AddModelError(string.Empty,
                "Não foi possível criar o funcionário. Verifique os dados e tente novamente.");
            await CarregarRoles(modelo.IdRole);
            return View(modelo);
        }

        await _gerenciadorUsuarioLocal.AddClaimAsync(novoUsuario,
            new Claim("NomeFuncionario", modelo.Nome));

        if (!string.IsNullOrWhiteSpace(modelo.Telefone))
            await _gerenciadorUsuarioLocal.SetPhoneNumberAsync(novoUsuario, modelo.Telefone);

        var role = await _gerenciadorRole.FindByIdAsync(modelo.IdRole);
        if (role?.Name != null)
            await _gerenciadorUsuarioLocal.AddToRoleAsync(novoUsuario, role.Name);

        var idEmpresa = await ObterIdEmpresaAsync();
        _contextoBase.EmpresaUsuario.Add(new EmpresaUsuario
        {
            IdEmpresa = idEmpresa,
            IdUsuario = novoUsuario.Id
        });
        await _contextoBase.SaveChangesAsync();

        DefinirToast($"Funcionário {modelo.Email} cadastrado com sucesso!", "success");
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id)
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        if (!await UsuarioPertenceEmpresa(id, idEmpresa)) return NotFound();

        var usuario = await _gerenciadorUsuarioLocal.FindByIdAsync(id);
        if (usuario == null) return NotFound();
        if (await _gerenciadorUsuarioLocal.IsInRoleAsync(usuario, "Admin")) return NotFound();

        var roles         = await _gerenciadorUsuarioLocal.GetRolesAsync(usuario);
        var nomeRoleAtual = roles.FirstOrDefault();
        var roleAtual     = nomeRoleAtual != null
            ? await _gerenciadorRole.FindByNameAsync(nomeRoleAtual)
            : null;

        var claims   = await _gerenciadorUsuarioLocal.GetClaimsAsync(usuario);
        var nomeAtual = claims.FirstOrDefault(c => c.Type == "NomeFuncionario")?.Value;

        var modelo = new UsuarioFormViewModel
        {
            IdUsuario = usuario.Id,
            Email     = usuario.Email!,
            IdRole    = roleAtual?.Id,
            Telefone  = usuario.PhoneNumber,
            Nome      = nomeAtual ?? ""
        };

        await CarregarRoles(roleAtual?.Id);
        return View(modelo);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, UsuarioFormViewModel modelo)
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        if (!await UsuarioPertenceEmpresa(id, idEmpresa)) return NotFound();

        var usuario = await _gerenciadorUsuarioLocal.FindByIdAsync(id);
        if (usuario == null) return NotFound();
        if (await _gerenciadorUsuarioLocal.IsInRoleAsync(usuario, "Admin")) return NotFound();

        ModelState.Remove(nameof(modelo.SenhaTemporaria));
        ModelState.Remove(nameof(modelo.ConfirmarSenha));

        ValidarTamanhoTexto(nameof(modelo.Nome),            modelo.Nome,            100);
        ValidarTamanhoTexto(nameof(modelo.Telefone),        modelo.Telefone,        20);
        ValidarTamanhoTexto(nameof(modelo.SenhaTemporaria), modelo.SenhaTemporaria, 128);

        if (!ModelState.IsValid)
        {
            await CarregarRoles(modelo.IdRole);
            return View(modelo);
        }

        var rolesAtuais = await _gerenciadorUsuarioLocal.GetRolesAsync(usuario);
        await _gerenciadorUsuarioLocal.RemoveFromRolesAsync(usuario, rolesAtuais);

        var novaRole = await _gerenciadorRole.FindByIdAsync(modelo.IdRole);
        if (novaRole?.Name != null)
            await _gerenciadorUsuarioLocal.AddToRoleAsync(usuario, novaRole.Name);

        var claimsAtuais = await _gerenciadorUsuarioLocal.GetClaimsAsync(usuario);
        var claimNome    = claimsAtuais.FirstOrDefault(c => c.Type == "NomeFuncionario");
        if (claimNome != null)
            await _gerenciadorUsuarioLocal.ReplaceClaimAsync(usuario, claimNome,
                new Claim("NomeFuncionario", modelo.Nome));
        else
            await _gerenciadorUsuarioLocal.AddClaimAsync(usuario,
                new Claim("NomeFuncionario", modelo.Nome));

        await _gerenciadorUsuarioLocal.SetPhoneNumberAsync(usuario, modelo.Telefone ?? "");

        if (!string.IsNullOrWhiteSpace(modelo.SenhaTemporaria))
        {
            var tokenRedefinicao = await _gerenciadorUsuarioLocal.GeneratePasswordResetTokenAsync(usuario);
            var resultadoSenha   = await _gerenciadorUsuarioLocal.ResetPasswordAsync(
                usuario, tokenRedefinicao, modelo.SenhaTemporaria);

            if (!resultadoSenha.Succeeded)
            {
                ModelState.AddModelError(string.Empty,
                    "Não foi possível alterar a senha. Verifique os requisitos mínimos.");
                await CarregarRoles(modelo.IdRole);
                return View(modelo);
            }
        }

        DefinirToast("Funcionário atualizado com sucesso!", "warning");
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(string id)
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        if (!await UsuarioPertenceEmpresa(id, idEmpresa)) return NotFound();

        var usuario = await _gerenciadorUsuarioLocal.FindByIdAsync(id);
        if (usuario == null) return NotFound();
        if (await _gerenciadorUsuarioLocal.IsInRoleAsync(usuario, "Admin")) return NotFound();

        var roles           = await _gerenciadorUsuarioLocal.GetRolesAsync(usuario);
        var claims          = await _gerenciadorUsuarioLocal.GetClaimsAsync(usuario);
        var nomeFuncionario = claims.FirstOrDefault(c => c.Type == "NomeFuncionario")?.Value;

        var modelo = new UsuarioRoleViewModel
        {
            IdUsuario = usuario.Id,
            Email     = usuario.Email!,
            Nome      = nomeFuncionario,
            NomeRole  = roles.FirstOrDefault(),
            EhAdmin   = false
        };
        return View(modelo);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(string id)
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        if (!await UsuarioPertenceEmpresa(id, idEmpresa)) return NotFound();

        var usuario = await _gerenciadorUsuarioLocal.FindByIdAsync(id);
        if (usuario == null) return NotFound();
        if (await _gerenciadorUsuarioLocal.IsInRoleAsync(usuario, "Admin")) return NotFound();

        var vinculo = await _contextoBase.EmpresaUsuario
            .FirstOrDefaultAsync(eu => eu.IdUsuario == id);
        if (vinculo != null)
            _contextoBase.EmpresaUsuario.Remove(vinculo);

        await _gerenciadorUsuarioLocal.DeleteAsync(usuario);
        await _contextoBase.SaveChangesAsync();

        DefinirToast("Funcionário removido com sucesso!", "danger");
        return RedirectToAction(nameof(Index));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task CarregarRoles(string? idSelecionado = null)
    {
        var roles = await _permissaoService.ListarRolesCustomizadasAsync();
        ViewData["IdRole"] = new SelectList(roles, "Id", "Name", idSelecionado);
    }

    private async Task<bool> UsuarioPertenceEmpresa(string idUsuario, int idEmpresa) =>
        await _contextoBase.EmpresaUsuario
            .AnyAsync(eu => eu.IdUsuario == idUsuario && eu.IdEmpresa == idEmpresa);
}

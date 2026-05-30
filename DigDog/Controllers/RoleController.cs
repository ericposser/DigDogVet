using DigDog.Data;
using DigDog.Models;
using DigDog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DigDog.Controllers;

[Authorize(Roles = "Admin")]
public class RoleController : UtilController
{
    private readonly RoleManager<IdentityRole> _gerenciadorRole;
    private readonly PermissaoService _permissaoService;

    public RoleController(
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao,
        Contexto contexto,
        RoleManager<IdentityRole> gerenciadorRole,
        PermissaoService permissaoService)
        : base(gerenciadorUsuario, provedorProtecao, contexto)
    {
        _gerenciadorRole  = gerenciadorRole;
        _permissaoService = permissaoService;
    }

    public async Task<IActionResult> Index()
    {
        var roles = await _permissaoService.ListarRolesCustomizadasAsync();
        return View(roles);
    }

    public IActionResult Create()
    {
        var modelo = new RoleFormViewModel
        {
            GruposPermissao = _permissaoService.MontarGruposPermissao(new())
        };
        return View(modelo);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RoleFormViewModel modelo)
    {
        if (!ModelState.IsValid)
        {
            modelo.GruposPermissao = _permissaoService.MontarGruposPermissao(modelo.PermissoesSelecionadas);
            return View(modelo);
        }

        if (await _gerenciadorRole.RoleExistsAsync(modelo.Nome.Trim()))
        {
            ModelState.AddModelError(nameof(modelo.Nome), "Já existe um perfil com este nome.");
            modelo.GruposPermissao = _permissaoService.MontarGruposPermissao(modelo.PermissoesSelecionadas);
            return View(modelo);
        }

        var novaRole  = new IdentityRole(modelo.Nome.Trim());
        var resultado = await _gerenciadorRole.CreateAsync(novaRole);

        if (!resultado.Succeeded)
        {
            foreach (var erro in resultado.Errors)
                ModelState.AddModelError(string.Empty, erro.Description);
            modelo.GruposPermissao = _permissaoService.MontarGruposPermissao(modelo.PermissoesSelecionadas);
            return View(modelo);
        }

        await _permissaoService.SalvarPermissoesRoleAsync(novaRole.Id, modelo.PermissoesSelecionadas);
        DefinirToast("Perfil criado com sucesso!", "success");
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id)
    {
        var role = await _gerenciadorRole.FindByIdAsync(id);
        if (role == null || role.Name == "Admin") return NotFound();

        var permissoesSelecionadas = await _permissaoService.ObterPermissoesRoleAsync(id);
        var modelo = new RoleFormViewModel
        {
            Id                    = role.Id,
            Nome                  = role.Name!,
            PermissoesSelecionadas = permissoesSelecionadas,
            GruposPermissao       = _permissaoService.MontarGruposPermissao(permissoesSelecionadas)
        };
        return View(modelo);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, RoleFormViewModel modelo)
    {
        var role = await _gerenciadorRole.FindByIdAsync(id);
        if (role == null || role.Name == "Admin") return NotFound();

        if (!ModelState.IsValid)
        {
            modelo.GruposPermissao = _permissaoService.MontarGruposPermissao(modelo.PermissoesSelecionadas);
            return View(modelo);
        }

        var roleExistente = await _gerenciadorRole.FindByNameAsync(modelo.Nome.Trim());
        if (roleExistente != null && roleExistente.Id != id)
        {
            ModelState.AddModelError(nameof(modelo.Nome), "Já existe um perfil com este nome.");
            modelo.GruposPermissao = _permissaoService.MontarGruposPermissao(modelo.PermissoesSelecionadas);
            return View(modelo);
        }

        role.Name           = modelo.Nome.Trim();
        role.NormalizedName = modelo.Nome.Trim().ToUpperInvariant();
        await _gerenciadorRole.UpdateAsync(role);
        await _permissaoService.SalvarPermissoesRoleAsync(id, modelo.PermissoesSelecionadas);

        DefinirToast("Perfil atualizado com sucesso!", "warning");
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(string id)
    {
        var role = await _gerenciadorRole.FindByIdAsync(id);
        if (role == null || role.Name == "Admin") return NotFound();
        return View(role);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(string id)
    {
        var role = await _gerenciadorRole.FindByIdAsync(id);
        if (role == null || role.Name == "Admin") return NotFound();

        await _permissaoService.RemoverTodasPermissoesRoleAsync(id);
        await _gerenciadorRole.DeleteAsync(role);

        DefinirToast("Perfil excluído com sucesso!", "danger");
        return RedirectToAction(nameof(Index));
    }
}
using DigDog.Data;
using DigDog.Filters;
using DigDog.Models;
using DigDog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigDog.Controllers;

[Authorize]
public class ClienteController : UtilController
{
    private readonly Contexto _contexto;
    private readonly LogService _logService;

    public ClienteController(
        Contexto contexto,
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao,
        LogService logService)
        : base(gerenciadorUsuario, provedorProtecao, contexto)
    {
        _contexto   = contexto;
        _logService = logService;
    }

    [RequerPermissao(Permissao.TutoresVisualizar)]
    public async Task<IActionResult> Index()
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        var clientes = await _contexto.Cliente
            .AsNoTracking()
            .Where(c => c.IdEmpresa == idEmpresa)
            .OrderBy(c => c.Nome)
            .ToListAsync();
        return View(clientes);
    }

    [RequerPermissao(Permissao.TutoresCriar)]
    public IActionResult Create() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.TutoresCriar)]
    public async Task<IActionResult> Create([Bind("Nome,Cpf,Telefone,Email,Endereco")] Cliente cliente)
    {
        cliente.IdEmpresa = await ObterIdEmpresaAsync();
        RemoverValidacaoUsuario();
        if (ModelState.IsValid)
        {
            _contexto.Add(cliente);
            await _contexto.SaveChangesAsync();
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Criou", "Tutores",
                $"Cadastrou o tutor {cliente.Nome}",
                cliente.IdEmpresa);
            DefinirToast("Tutor cadastrado com sucesso!", "success");
            return RedirectToAction(nameof(Index));
        }
        return View(cliente);
    }

    [RequerPermissao(Permissao.TutoresEditar)]
    public async Task<IActionResult> Edit(string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var cliente = await _contexto.Cliente
            .AsNoTracking()
            .Where(c => c.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(c => c.Id == idReal);
        if (cliente == null) return NotFound();
        ViewBag.IdCriptografado = CriptografarId(cliente.Id);
        return View(cliente);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.TutoresEditar)]
    public async Task<IActionResult> Edit(string id, [Bind("Nome,Cpf,Telefone,Email,Endereco")] Cliente cliente)
    {
        var idRota = RouteData.Values["id"]?.ToString();
        var idReal = DescriptografarId(idRota ?? id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        RemoverValidacaoUsuario();
        var clienteExistente = await _contexto.Cliente
            .Where(c => c.IdEmpresa == idEmpresa && c.Id == idReal)
            .FirstOrDefaultAsync();
        if (clienteExistente == null) return NotFound();
        if (ModelState.IsValid)
        {
            clienteExistente.Nome     = cliente.Nome;
            clienteExistente.Cpf      = cliente.Cpf;
            clienteExistente.Telefone = cliente.Telefone;
            clienteExistente.Email    = cliente.Email;
            clienteExistente.Endereco = cliente.Endereco;
            try { await _contexto.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                if (!await ClienteExisteAsync(idReal.Value, idEmpresa)) return NotFound();
                throw;
            }
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Editou", "Tutores",
                $"Atualizou o tutor {clienteExistente.Nome}",
                idEmpresa);
            DefinirToast("Tutor atualizado com sucesso!", "warning");
            return RedirectToAction(nameof(Index));
        }
        ViewBag.IdCriptografado = CriptografarId(idReal.Value);
        return View(cliente);
    }

    [RequerPermissao(Permissao.TutoresExcluir)]
    public async Task<IActionResult> Delete(string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var cliente = await _contexto.Cliente
            .AsNoTracking()
            .Where(c => c.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(c => c.Id == idReal);
        if (cliente == null) return NotFound();
        ViewBag.IdCriptografado = CriptografarId(cliente.Id);
        return View(cliente);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.TutoresExcluir)]
    public async Task<IActionResult> DeleteConfirmed([FromRoute] string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var cliente = await _contexto.Cliente
            .Where(c => c.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(c => c.Id == idReal);
        if (cliente != null)
        {
            var nome = cliente.Nome;
            _contexto.Cliente.Remove(cliente);
            await _contexto.SaveChangesAsync();
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Excluiu", "Tutores",
                $"Removeu o tutor {nome}",
                idEmpresa);
        }
        DefinirToast("Tutor excluído com sucesso!", "danger");
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> ClienteExisteAsync(int id, int idEmpresa) =>
        await _contexto.Cliente.AnyAsync(c => c.Id == id && c.IdEmpresa == idEmpresa);
}
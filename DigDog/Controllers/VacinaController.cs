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
public class VacinaController : UtilController
{
    private readonly Contexto _contexto;
    private readonly LogService _logService;

    public VacinaController(
        Contexto contexto,
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao,
        LogService logService)
        : base(gerenciadorUsuario, provedorProtecao, contexto)
    {
        _contexto   = contexto;
        _logService = logService;
    }

    [RequerPermissao(Permissao.VacinasVisualizar)]
    public async Task<IActionResult> Index()
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        var vacinas = await _contexto.Vacina
            .Where(v => v.IdEmpresa == idEmpresa)
            .OrderBy(v => v.DataValidade)
            .ToListAsync();
        return View(vacinas);
    }

    [RequerPermissao(Permissao.VacinasCriar)]
    public IActionResult Create() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.VacinasCriar)]
    public async Task<IActionResult> Create([Bind("Nome,Fabricante,Lote,DataValidade")] Vacina vacina)
    {
        vacina.IdEmpresa = await ObterIdEmpresaAsync();
        RemoverValidacaoUsuario();
        if (vacina.DataValidade < DateTime.Today)
            ModelState.AddModelError(nameof(vacina.DataValidade), "Não é possível cadastrar uma vacina com o frasco já vencido.");
        if (ModelState.IsValid)
        {
            _contexto.Add(vacina);
            await _contexto.SaveChangesAsync();
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Criou", "Vacinas",
                $"Cadastrou a vacina {vacina.Nome}",
                vacina.IdEmpresa);
            DefinirToast("Vacina cadastrada no catálogo com sucesso!", "success");
            return RedirectToAction(nameof(Index));
        }
        return View(vacina);
    }

    [RequerPermissao(Permissao.VacinasEditar)]
    public async Task<IActionResult> Edit(string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var vacina = await _contexto.Vacina
            .Where(v => v.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(v => v.Id == idReal);
        if (vacina == null) return NotFound();
        ViewBag.IdCriptografado = CriptografarId(vacina.Id);
        return View(vacina);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.VacinasEditar)]
    public async Task<IActionResult> Edit(string id, [Bind("Nome,Fabricante,Lote,DataValidade")] Vacina vacina)
    {
        var idRota = RouteData.Values["id"]?.ToString();
        var idReal = DescriptografarId(idRota ?? id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        RemoverValidacaoUsuario();
        if (vacina.DataValidade < DateTime.Today)
            ModelState.AddModelError(nameof(vacina.DataValidade), "Não é possível cadastrar uma vacina com o frasco já vencido.");
        var vacinaExistente = await _contexto.Vacina
            .Where(v => v.IdEmpresa == idEmpresa && v.Id == idReal)
            .FirstOrDefaultAsync();
        if (vacinaExistente == null) return NotFound();
        if (ModelState.IsValid)
        {
            vacinaExistente.Nome        = vacina.Nome;
            vacinaExistente.Fabricante  = vacina.Fabricante;
            vacinaExistente.Lote        = vacina.Lote;
            vacinaExistente.DataValidade = vacina.DataValidade;
            try { await _contexto.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                if (!VacinaExiste(idReal.Value, idEmpresa)) return NotFound();
                throw;
            }
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Editou", "Vacinas",
                $"Atualizou a vacina {vacinaExistente.Nome}",
                idEmpresa);
            DefinirToast("Vacina atualizada com sucesso!", "warning");
            return RedirectToAction(nameof(Index));
        }
        ViewBag.IdCriptografado = CriptografarId(idReal.Value);
        return View(vacina);
    }

    [RequerPermissao(Permissao.VacinasExcluir)]
    public async Task<IActionResult> Delete(string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var vacina = await _contexto.Vacina
            .Where(v => v.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(v => v.Id == idReal);
        if (vacina == null) return NotFound();
        ViewBag.IdCriptografado = CriptografarId(vacina.Id);
        return View(vacina);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.VacinasExcluir)]
    public async Task<IActionResult> DeleteConfirmed([FromRoute] string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var vacina = await _contexto.Vacina
            .Where(v => v.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(v => v.Id == idReal);
        if (vacina != null)
        {
            var nome = vacina.Nome;
            _contexto.Vacina.Remove(vacina);
            await _contexto.SaveChangesAsync();
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Excluiu", "Vacinas",
                $"Removeu a vacina {nome} do catálogo",
                idEmpresa);
        }
        DefinirToast("Vacina removida do catálogo com sucesso!", "danger");
        return RedirectToAction(nameof(Index));
    }

    private bool VacinaExiste(int id, int idEmpresa) =>
        _contexto.Vacina.Any(v => v.Id == id && v.IdEmpresa == idEmpresa);
}
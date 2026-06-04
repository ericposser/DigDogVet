using DigDog.Data;
using DigDog.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DigDog.Controllers;

[Authorize(Roles = "Admin")]
public class LogController : UtilController
{
    // Limite de logs exibidos por carregamento de página.
    // Os logs mais antigos continuam no banco e são limpos pelo LimpezaLogService.
    private const int MaximoLogsExibidos = 500;

    private readonly Contexto _contexto;

    public LogController(
        Contexto contexto,
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao,
        IMemoryCache cache)
        : base(gerenciadorUsuario, provedorProtecao, contexto, cache)
    {
        _contexto = contexto;
    }

    public async Task<IActionResult> Index()
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        var logs      = await _contexto.Log
            .AsNoTracking()
            .Where(l => l.IdEmpresa == idEmpresa)
            .OrderByDescending(l => l.DataHora)
            .Take(MaximoLogsExibidos)
            .ToListAsync();

        ViewBag.LimiteLogs = MaximoLogsExibidos;
        return View(logs);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();

        var idEmpresa = await ObterIdEmpresaAsync();

        // DELETE direto via SQL — sem load + tracking + delete
        var linhasAfetadas = await _contexto.Log
            .Where(l => l.Id == idReal && l.IdEmpresa == idEmpresa)
            .ExecuteDeleteAsync();

        if (linhasAfetadas == 0)
            return NotFound();

        DefinirToast("Registro removido com sucesso!", "danger");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LimparTodos()
    {
        var idEmpresa = await ObterIdEmpresaAsync();

        // DELETE em massa: uma única instrução SQL em vez de N deletes
        var linhasRemovidas = await _contexto.Log
            .Where(l => l.IdEmpresa == idEmpresa)
            .ExecuteDeleteAsync();

        DefinirToast($"{linhasRemovidas} registro(s) removido(s)!", "danger");
        return RedirectToAction(nameof(Index));
    }
}
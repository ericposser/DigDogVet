using DigDog.Data;
using DigDog.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigDog.Controllers;

[Authorize(Roles = "Admin")]
public class LogController : UtilController
{
    private readonly Contexto _contexto;

    public LogController(
        Contexto contexto,
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao)
        : base(gerenciadorUsuario, provedorProtecao, contexto)
    {
        _contexto = contexto;
    }

    public async Task<IActionResult> Index()
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        var logs      = await _contexto.Log
            .Where(l => l.IdEmpresa == idEmpresa)
            .OrderByDescending(l => l.DataHora)
            .ToListAsync();
        return View(logs);
    }

    // ── Delete individual ──────────────────────────────────────────────────
    // Recebe ID criptografado (string), não int diretamente.
    // Verifica que o log pertence à empresa do Admin antes de remover.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();

        var idEmpresa = await ObterIdEmpresaAsync();
        var log       = await _contexto.Log
            .Where(l => l.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(l => l.Id == idReal);

        if (log != null)
        {
            _contexto.Log.Remove(log);
            await _contexto.SaveChangesAsync();
        }

        DefinirToast("Registro removido com sucesso!", "danger");
        return RedirectToAction(nameof(Index));
    }

    // ── Limpar todos ───────────────────────────────────────────────────────
    // Sem alteração de lógica — já filtrava por empresa.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LimparTodos()
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        var todos     = _contexto.Log.Where(l => l.IdEmpresa == idEmpresa);
        _contexto.Log.RemoveRange(todos);
        await _contexto.SaveChangesAsync();
        DefinirToast("Todos os registros foram removidos!", "danger");
        return RedirectToAction(nameof(Index));
    }
}

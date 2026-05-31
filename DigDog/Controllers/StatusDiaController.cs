using DigDog.Data;
using DigDog.Filters;
using DigDog.Hubs;
using DigDog.Models;
using DigDog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DigDog.Controllers;

[Authorize]
public class StatusDiaController : UtilController
{
    private readonly Contexto _contexto;
    private readonly UserManager<IdentityUser> _gerenciador;
    private readonly IHubContext<KanbanHub> _hubContext;
    private readonly LogService _logService;

    public StatusDiaController(
        Contexto contexto,
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao,
        IHubContext<KanbanHub> hubContext,
        LogService logService)
        : base(gerenciadorUsuario, provedorProtecao, contexto)
    {
        _contexto    = contexto;
        _gerenciador = gerenciadorUsuario;
        _hubContext  = hubContext;
        _logService  = logService;
    }

    [RequerPermissao(Permissao.StatusDiaVisualizar)]
    public async Task<IActionResult> Index()
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        var hoje      = DateTime.Today;

        var banhos = await _contexto.BanhoTosa
            .Include(b => b.Pet)
            .Where(b => b.IdEmpresa == idEmpresa && b.DataHora.Date == hoje)
            .OrderBy(b => b.DataHora)
            .ToListAsync();

        var idsDosBanhos = banhos.Select(b => b.Id).ToList();

        var tokensAtivos = await _contexto.KanbanToken
            .Where(t =>
                t.IdEmpresa == idEmpresa &&
                t.Ativo     &&
                t.ExpiraEm  > DateTime.UtcNow &&
                idsDosBanhos.Contains(t.IdBanhoTosa))
            .ToListAsync();

        var dicionarioLinks = tokensAtivos.ToDictionary(
            t => t.IdBanhoTosa,
            t => Url.Action("Publico", "StatusDia",
                     new { token = t.Token }, Request.Scheme)!);

        ViewBag.TokensAtivos = dicionarioLinks;
        ViewBag.DataHoje     = hoje.ToString(
            "dddd, dd 'de' MMMM 'de' yyyy",
            new System.Globalization.CultureInfo("pt-BR"));

        return View(banhos);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GerarLinkPet(string id)
    {
        var idReal    = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();

        var banho = await _contexto.BanhoTosa
            .Include(b => b.Pet)
            .Where(b => b.IdEmpresa == idEmpresa && b.Id == idReal)
            .FirstOrDefaultAsync();
        if (banho == null) return NotFound();

        var tokensAntigos = await _contexto.KanbanToken
            .Where(t => t.IdBanhoTosa == idReal && t.IdEmpresa == idEmpresa && t.Ativo)
            .ToListAsync();
        foreach (var t in tokensAntigos) t.Ativo = false;

        var novoToken = Guid.NewGuid().ToString("D");
        var hash      = KanbanHub.GerarHash(novoToken);

        _contexto.KanbanToken.Add(new KanbanToken
        {
            IdEmpresa   = idEmpresa,
            IdBanhoTosa = idReal.Value,
            Token       = novoToken,
            TokenHash   = hash,
            CriadoEm    = DateTime.UtcNow,
            ExpiraEm    = DateTime.Today.AddDays(1).ToUniversalTime(),
            Ativo       = true
        });

        await _contexto.SaveChangesAsync();

        await _logService.RegistrarAsync(
            await ObterNomeFuncionarioAsync(),
            "Gerou Link", "Status do Dia",
            $"Gerou link de status para {banho.Pet?.Nome} — {banho.DataHora:dd/MM/yyyy HH:mm}",
            idEmpresa);

        DefinirToast($"Link gerado para {banho.Pet?.Nome}!", "success");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevogarLinkPet(string id)
    {
        var idReal    = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();

        var banho = await _contexto.BanhoTosa
            .Include(b => b.Pet)
            .Where(b => b.IdEmpresa == idEmpresa && b.Id == idReal)
            .FirstOrDefaultAsync();

        var tokens = await _contexto.KanbanToken
            .Where(t => t.IdBanhoTosa == idReal && t.IdEmpresa == idEmpresa && t.Ativo)
            .ToListAsync();
        foreach (var t in tokens) t.Ativo = false;

        await _contexto.SaveChangesAsync();

        await _logService.RegistrarAsync(
            await ObterNomeFuncionarioAsync(),
            "Revogou Link", "Status do Dia",
            $"Revogou link de status para {banho?.Pet?.Nome ?? $"Agendamento #{idReal}"}",
            idEmpresa);

        DefinirToast("Link revogado com sucesso.", "warning");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AtualizarStatus(string id, int novoStatus)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();

        // Rejeita valores fora do enum antes de qualquer consulta ao banco
        if (!Enum.IsDefined(typeof(StatusKanban), novoStatus)) return BadRequest();

        var idEmpresa = await ObterIdEmpresaAsync();
        var banho     = await _contexto.BanhoTosa
            .Include(b => b.Pet)
            .Where(b => b.Id == idReal && b.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync();
        if (banho == null) return NotFound();

        var statusAnterior = banho.StatusKanban.ToString();
        banho.StatusKanban = (StatusKanban)novoStatus;
        await _contexto.SaveChangesAsync();

        await _logService.RegistrarAsync(
            await ObterNomeFuncionarioAsync(),
            "Editou", "Status do Dia",
            $"Atualizou status de {banho.Pet?.Nome} de {statusAnterior} para {banho.StatusKanban}",
            idEmpresa);

        var token = await _contexto.KanbanToken
            .Where(t => t.IdBanhoTosa == idReal && t.Ativo && t.ExpiraEm > DateTime.UtcNow)
            .FirstOrDefaultAsync();

        if (token != null)
        {
            var hash = KanbanHub.GerarHash(token.Token);
            await _hubContext.Clients
                .Group($"kanban-{hash}")
                .SendAsync("CardMovido", idReal.Value, novoStatus);
        }

        return Ok();
    }

    // ── Rota pública ───────────────────────────────────────────────────────
    // Rate limit restrito (política "publica": 30 req/min por IP).
    // Valida formato do token antes de qualquer acesso ao banco.
    // Resposta uniforme para token inválido e não encontrado/expirado.
    [AllowAnonymous]
    [EnableRateLimiting("publica")]
    public async Task<IActionResult> Publico(string token)
    {
        if (!TokenPublicoValido(token)) return View("LinkInvalido");

        var hash = KanbanHub.GerarHash(token);

        var kanbanToken = await _contexto.KanbanToken
            .Include(t => t.BanhoTosa)
                .ThenInclude(b => b!.Pet)
            .FirstOrDefaultAsync(t =>
                t.TokenHash == hash &&
                t.Ativo     &&
                t.ExpiraEm  > DateTime.UtcNow);

        // Mesma view para token inválido, expirado ou não encontrado
        if (kanbanToken == null) return View("LinkInvalido");

        var (nome, fotoBytes, fotoMime) = await ObterDadosEstabelecimento(kanbanToken.IdEmpresa);

        // EhDono removido — a view pública é somente leitura para todos.
        // Edição de status ocorre exclusivamente pela tela interna (AtualizarStatus).
        ViewBag.TokenPublico        = token;
        ViewBag.NomeEstabelecimento = nome;
        ViewBag.FotoBytes           = fotoBytes;
        ViewBag.FotoMime            = fotoMime;
        ViewBag.DataHoje            = DateTime.Today.ToString(
            "dddd, dd 'de' MMMM 'de' yyyy",
            new System.Globalization.CultureInfo("pt-BR"));

        return View(kanbanToken.BanhoTosa!);
    }

    private async Task<(string Nome, byte[]? FotoDados, string? FotoMime)> ObterDadosEstabelecimento(int idEmpresa)
    {
        var empresa = await _contexto.Empresa.FindAsync(idEmpresa);
        var config  = await _contexto.Configuracao
            .Where(c => c.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync();

        var nome = !string.IsNullOrWhiteSpace(config?.NomeEstabelecimento)
            ? config.NomeEstabelecimento
            : !string.IsNullOrWhiteSpace(empresa?.NomeEstabelecimento)
                ? empresa.NomeEstabelecimento
                : "DigDogVet";

        return (nome, config?.FotoDados, config?.FotoMimeType);
    }
}

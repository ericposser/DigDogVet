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
using Microsoft.Extensions.Caching.Memory;

namespace DigDog.Controllers;

[Authorize]
public class StatusDiaController : UtilController
{
    private readonly Contexto _contexto;
    private readonly UserManager<IdentityUser> _gerenciador;
    private readonly IHubContext<KanbanHub> _hubContext;
    private readonly LogService _logService;
    private readonly IMemoryCache _cache;

    public StatusDiaController(
        Contexto contexto,
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao,
        IHubContext<KanbanHub> hubContext,
        LogService logService,
        IMemoryCache cache)
        : base(gerenciadorUsuario, provedorProtecao, contexto, cache)
    {
        _contexto    = contexto;
        _gerenciador = gerenciadorUsuario;
        _hubContext  = hubContext;
        _logService  = logService;
        _cache       = cache;
    }

    [RequerPermissao(Permissao.StatusDiaVisualizar)]
    public async Task<IActionResult> Index()
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        var hoje      = DateTime.Today;

        var banhos = await _contexto.BanhoTosa
            .AsNoTracking()
            .Include(b => b.Pet)
            .Where(b => b.IdEmpresa == idEmpresa && b.DataHora.Date == hoje)
            .OrderBy(b => b.DataHora)
            .ToListAsync();

        var idsDosBanhos = banhos.Select(b => b.Id).ToList();

        var tokensAtivos = await _contexto.KanbanToken
            .AsNoTracking()
            .Where(t =>
                t.IdEmpresa == idEmpresa &&
                t.Ativo     &&
                t.ExpiraEm  > DateTime.UtcNow &&
                idsDosBanhos.Contains(t.IdBanhoTosa))
            .Select(t => new { t.IdBanhoTosa, t.Token })
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
            .Select(b => new { b.Id, b.DataHora, NomePet = b.Pet!.Nome })
            .FirstOrDefaultAsync();
        if (banho == null) return NotFound();

        // Desativa tokens antigos em uma query
        await _contexto.KanbanToken
            .Where(t => t.IdBanhoTosa == idReal && t.IdEmpresa == idEmpresa && t.Ativo)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Ativo, false));

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
            $"Gerou link de status para {banho.NomePet} — {banho.DataHora:dd/MM/yyyy HH:mm}",
            idEmpresa);

        DefinirToast($"Link gerado para {banho.NomePet}!", "success");
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
            .Where(b => b.IdEmpresa == idEmpresa && b.Id == idReal)
            .Select(b => new { NomePet = b.Pet!.Nome })
            .FirstOrDefaultAsync();

        await _contexto.KanbanToken
            .Where(t => t.IdBanhoTosa == idReal && t.IdEmpresa == idEmpresa && t.Ativo)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Ativo, false));

        await _logService.RegistrarAsync(
            await ObterNomeFuncionarioAsync(),
            "Revogou Link", "Status do Dia",
            $"Revogou link de status para {banho?.NomePet ?? $"Agendamento #{idReal}"}",
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
            .AsNoTracking()
            .Where(t => t.IdBanhoTosa == idReal && t.Ativo && t.ExpiraEm > DateTime.UtcNow)
            .Select(t => t.Token)
            .FirstOrDefaultAsync();

        if (token != null)
        {
            var hash = KanbanHub.GerarHash(token);
            await _hubContext.Clients
                .Group($"kanban-{hash}")
                .SendAsync("CardMovido", idReal.Value, novoStatus);
        }

        return Ok();
    }

    [AllowAnonymous]
    [EnableRateLimiting("publica")]
    public async Task<IActionResult> Publico(string token)
    {
        if (!TokenPublicoValido(token)) return View("LinkInvalido");

        var hash = KanbanHub.GerarHash(token);

        var kanbanToken = await _contexto.KanbanToken
            .AsNoTracking()
            .Include(t => t.BanhoTosa)
                .ThenInclude(b => b!.Pet)
            .FirstOrDefaultAsync(t =>
                t.TokenHash == hash &&
                t.Ativo     &&
                t.ExpiraEm  > DateTime.UtcNow);

        if (kanbanToken == null) return View("LinkInvalido");

        var (nome, fotoBytes, fotoMime) = await ObterDadosEstabelecimentoAsync(kanbanToken.IdEmpresa);

        ViewBag.TokenPublico        = token;
        ViewBag.NomeEstabelecimento = nome;
        ViewBag.FotoBytes           = fotoBytes;
        ViewBag.FotoMime            = fotoMime;
        ViewBag.DataHoje            = DateTime.Today.ToString(
            "dddd, dd 'de' MMMM 'de' yyyy",
            new System.Globalization.CultureInfo("pt-BR"));

        return View(kanbanToken.BanhoTosa!);
    }

    // ─── Cache compartilhado com CarteiraController (mesma chave) ────────
    private async Task<(string Nome, byte[]? FotoDados, string? FotoMime)> ObterDadosEstabelecimentoAsync(int idEmpresa)
    {
        var chaveCache = $"estabelecimento:{idEmpresa}";

        if (_cache.TryGetValue<(string, byte[]?, string?)>(chaveCache, out var dadosCache))
            return dadosCache;

        var nomeEmpresa = await _contexto.Empresa
            .AsNoTracking()
            .Where(e => e.Id == idEmpresa)
            .Select(e => e.NomeEstabelecimento)
            .FirstOrDefaultAsync();

        var config = await _contexto.Configuracao
            .AsNoTracking()
            .Where(c => c.IdEmpresa == idEmpresa)
            .Select(c => new { c.NomeEstabelecimento, c.FotoDados, c.FotoMimeType })
            .FirstOrDefaultAsync();

        var nome = !string.IsNullOrWhiteSpace(config?.NomeEstabelecimento)
            ? config.NomeEstabelecimento
            : !string.IsNullOrWhiteSpace(nomeEmpresa) ? nomeEmpresa : "DigDogVet";

        var resultado = (nome, config?.FotoDados, config?.FotoMimeType);
        _cache.Set(chaveCache, resultado, TimeSpan.FromMinutes(10));
        return resultado;
    }

    // Stub do método de validação — adapte se ele já existir em outro lugar
    private static bool TokenPublicoValido(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        return Guid.TryParse(token, out _);
    }
}
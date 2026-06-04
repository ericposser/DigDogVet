using DigDog.Data;
using DigDog.Models;
using DigDog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;

namespace DigDog.Controllers;

[Authorize]
public class CarteiraController : UtilController
{
    private readonly Contexto _contexto;
    private readonly LogService _logService;
    private readonly IMemoryCache _cache;

    public CarteiraController(
        Contexto contexto,
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao,
        LogService logService,
        IMemoryCache cache)
        : base(gerenciadorUsuario, provedorProtecao, contexto, cache)
    {
        _contexto   = contexto;
        _logService = logService;
        _cache      = cache;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GerarLink(string id)
    {
        var idReal    = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();

        var pet = await _contexto.Pet
            .Where(p => p.IdEmpresa == idEmpresa && p.Id == idReal)
            .Select(p => new { p.Id, p.Nome })
            .FirstOrDefaultAsync();
        if (pet == null) return NotFound();

        // Desativa tokens antigos em uma query (sem carregar entidades)
        await _contexto.CarteiraToken
            .Where(t => t.IdPet == idReal && t.IdEmpresa == idEmpresa && t.Ativo)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Ativo, false));

        var novoToken = Guid.NewGuid().ToString("D");
        var hash      = GerarHash(novoToken);

        _contexto.CarteiraToken.Add(new CarteiraToken
        {
            IdEmpresa = idEmpresa,
            IdPet     = idReal.Value,
            Token     = novoToken,
            TokenHash = hash,
            CriadoEm  = DateTime.UtcNow,
            Ativo     = true
        });

        await _contexto.SaveChangesAsync();

        await _logService.RegistrarAsync(
            await ObterNomeFuncionarioAsync(),
            "Gerou Link", "Carteira de Vacinação",
            $"Gerou link público da carteirinha de {pet.Nome}",
            idEmpresa);

        DefinirToast($"Link da carteirinha de {pet.Nome} gerado com sucesso!", "success");
        return RedirectToAction("Index", "Vacinacao");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevogarLink(string id)
    {
        var idReal    = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();

        var pet = await _contexto.Pet
            .Where(p => p.IdEmpresa == idEmpresa && p.Id == idReal)
            .Select(p => new { p.Id, p.Nome })
            .FirstOrDefaultAsync();

        await _contexto.CarteiraToken
            .Where(t => t.IdPet == idReal && t.IdEmpresa == idEmpresa && t.Ativo)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Ativo, false));

        await _logService.RegistrarAsync(
            await ObterNomeFuncionarioAsync(),
            "Revogou Link", "Carteira de Vacinação",
            $"Revogou link público da carteirinha de {pet?.Nome ?? $"Pet #{idReal}"}",
            idEmpresa);

        DefinirToast("Link da carteirinha revogado com sucesso.", "warning");
        return RedirectToAction("Index", "Vacinacao");
    }

    // ── Rota pública ───────────────────────────────────────────────────────
    [AllowAnonymous]
    [EnableRateLimiting("publica")]
    public async Task<IActionResult> Publico(string token)
    {
        if (!TokenPublicoValido(token)) return View("LinkInvalido");

        var hash = GerarHash(token);

        var carteiraToken = await _contexto.CarteiraToken
            .AsNoTracking()
            .Include(t => t.Pet)
                .ThenInclude(p => p!.Cliente)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Ativo);

        if (carteiraToken == null) return View("LinkInvalido");

        var vacinacoes = await _contexto.Vacinacao
            .AsNoTracking()
            .Include(v => v.Vacina)
            .Where(v => v.IdPet == carteiraToken.IdPet && v.IdEmpresa == carteiraToken.IdEmpresa)
            .OrderByDescending(v => v.DataAplicacao)
            .ToListAsync();

        var (nome, fotoBytes, fotoMime) = await ObterDadosEstabelecimentoAsync(carteiraToken.IdEmpresa);

        ViewBag.NomeEstabelecimento = nome;
        ViewBag.FotoBytes           = fotoBytes;
        ViewBag.FotoMime            = fotoMime;
        ViewBag.Token               = token;

        return View((carteiraToken.Pet, vacinacoes));
    }

    public static string GerarHash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ─── Cache de 10 minutos: dados do estabelecimento mudam raramente ───
    private async Task<(string Nome, byte[]? FotoDados, string? FotoMime)> ObterDadosEstabelecimentoAsync(int idEmpresa)
    {
        var chaveCache = $"estabelecimento:{idEmpresa}";

        if (_cache.TryGetValue<(string, byte[]?, string?)>(chaveCache, out var dadosCache))
            return dadosCache;

        var empresa = await _contexto.Empresa
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
            : !string.IsNullOrWhiteSpace(empresa) ? empresa : "DigDogVet";

        var resultado = (nome, config?.FotoDados, config?.FotoMimeType);
        _cache.Set(chaveCache, resultado, TimeSpan.FromMinutes(10));
        return resultado;
    }

    // Stub do método de validação — adapte se ele já existir em outro lugar
    private static bool TokenPublicoValido(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        // Formato esperado: GUID (36 chars com hífens). Bloqueia inputs maliciosos.
        return Guid.TryParse(token, out _);
    }
}
using DigDog.Data;
using DigDog.Models;
using DigDog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace DigDog.Controllers;

[Authorize]
public class CarteiraController : UtilController
{
    private readonly Contexto _contexto;
    private readonly LogService _logService;

    public CarteiraController(
        Contexto contexto,
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao,
        LogService logService)
        : base(gerenciadorUsuario, provedorProtecao, contexto)
    {
        _contexto   = contexto;
        _logService = logService;
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
            .FirstOrDefaultAsync();
        if (pet == null) return NotFound();

        var tokensAntigos = await _contexto.CarteiraToken
            .Where(t => t.IdPet == idReal && t.IdEmpresa == idEmpresa && t.Ativo)
            .ToListAsync();
        foreach (var t in tokensAntigos) t.Ativo = false;

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
            .FirstOrDefaultAsync();

        var tokens = await _contexto.CarteiraToken
            .Where(t => t.IdPet == idReal && t.IdEmpresa == idEmpresa && t.Ativo)
            .ToListAsync();
        foreach (var t in tokens) t.Ativo = false;

        await _contexto.SaveChangesAsync();

        await _logService.RegistrarAsync(
            await ObterNomeFuncionarioAsync(),
            "Revogou Link", "Carteira de Vacinação",
            $"Revogou link público da carteirinha de {pet?.Nome ?? $"Pet #{idReal}"}",
            idEmpresa);

        DefinirToast("Link da carteirinha revogado com sucesso.", "warning");
        return RedirectToAction("Index", "Vacinacao");
    }

    // ── Rota pública ───────────────────────────────────────────────────────
    // Rate limit restrito: 30 req/min por IP (política "publica" do Program.cs).
    // Não retorna diferença entre "token inválido" e "token não encontrado"
    // para não permitir enumeração.
    [AllowAnonymous]
    [EnableRateLimiting("publica")]
    public async Task<IActionResult> Publico(string token)
    {
        // Valida formato antes de qualquer acesso ao banco
        if (!TokenPublicoValido(token)) return View("LinkInvalido");

        var hash = GerarHash(token);

        var carteiraToken = await _contexto.CarteiraToken
            .Include(t => t.Pet)
                .ThenInclude(p => p!.Cliente)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Ativo);

        // Mesma view para token inválido e não encontrado — sem vazar informação
        if (carteiraToken == null) return View("LinkInvalido");

        var vacinacoes = await _contexto.Vacinacao
            .Include(v => v.Vacina)
            .Where(v => v.IdPet == carteiraToken.IdPet && v.IdEmpresa == carteiraToken.IdEmpresa)
            .OrderByDescending(v => v.DataAplicacao)
            .ToListAsync();

        var (nome, fotoBytes, fotoMime) = await ObterDadosEstabelecimento(carteiraToken.IdEmpresa);

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

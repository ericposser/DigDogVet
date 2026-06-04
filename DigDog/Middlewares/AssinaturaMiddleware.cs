using DigDog.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DigDog.Middlewares;

public class AssinaturaMiddleware
{
    private static readonly TimeSpan TtlIdEmpresa  = TimeSpan.FromHours(1);
    private static readonly TimeSpan TtlAssinatura = TimeSpan.FromMinutes(5);

    private readonly RequestDelegate _proximo;

    private static readonly string[] RotasLiberadas =
    {
        "/Identity",
        "/Assinatura/Inativa",
        "/assinaturaHub",
        "/kanbanHub",
        "/favicon.ico",
        "/Erro",
        "/Carteira/Publico",
        "/StatusDia/Publico",
        "/Webhook",
    };

    public AssinaturaMiddleware(RequestDelegate proximo)
    {
        _proximo = proximo;
    }

    public async Task InvokeAsync(
        HttpContext contexto,
        Contexto db,
        UserManager<IdentityUser> gerenciadorUsuario,
        IMemoryCache cache)
    {
        if (contexto.User.Identity?.IsAuthenticated != true)
        {
            await _proximo(contexto);
            return;
        }

        var caminho = contexto.Request.Path.Value ?? "";

        if (RotasLiberadas.Any(r => caminho.StartsWith(r, StringComparison.OrdinalIgnoreCase))
            || EhArquivoEstatico(caminho))
        {
            await _proximo(contexto);
            return;
        }

        var idUsuario = gerenciadorUsuario.GetUserId(contexto.User);
        if (idUsuario == null)
        {
            await _proximo(contexto);
            return;
        }

        // ─── 1. IdEmpresa (cache compartilhado com UtilController) ─────────
        var chaveEmpresa = $"idEmpresa:{idUsuario}";
        int idEmpresa;

        if (cache.TryGetValue<int>(chaveEmpresa, out var idEmpresaCache))
        {
            idEmpresa = idEmpresaCache;
        }
        else
        {
            var vinculo = await db.EmpresaUsuario
                .AsNoTracking()
                .Where(eu => eu.IdUsuario == idUsuario)
                .Select(eu => (int?)eu.IdEmpresa)
                .FirstOrDefaultAsync();

            if (vinculo == null)
            {
                contexto.Response.Redirect("/Assinatura/Inativa");
                return;
            }

            idEmpresa = vinculo.Value;
            cache.Set(chaveEmpresa, idEmpresa, TtlIdEmpresa);
        }

        // ─── 2. Status da assinatura (cache de 5 min) ──────────────────────
        var chaveAssinatura = $"assinatura-status:{idEmpresa}";
        StatusAssinaturaCache status;

        if (cache.TryGetValue<StatusAssinaturaCache>(chaveAssinatura, out var statusCache) && statusCache != null)
        {
            status = statusCache;
        }
        else
        {
            var dadosAssinatura = await db.Assinatura
                .AsNoTracking()
                .Where(a => a.IdEmpresa == idEmpresa)
                .OrderByDescending(a => a.Id)
                .Select(a => new { a.Ativa, a.DataExpiracao })
                .FirstOrDefaultAsync();

            status = dadosAssinatura != null
                ? new StatusAssinaturaCache(dadosAssinatura.Ativa, dadosAssinatura.DataExpiracao)
                : new StatusAssinaturaCache(false, DateTime.MinValue);

            cache.Set(chaveAssinatura, status, TtlAssinatura);
        }

        // A avaliação de DataExpiracao usa "agora" — então mesmo com cache,
        // expiração natural é detectada imediatamente sem precisar invalidar.
        var ativa = status.Ativa && status.DataExpiracao >= DateTime.Now;

        if (!ativa)
        {
            contexto.Response.Redirect("/Assinatura/Inativa");
            return;
        }

        await _proximo(contexto);
    }

    private static bool EhArquivoEstatico(string caminho)
    {
        var extensoesEstaticas = new[]
        {
            ".js", ".css", ".png", ".jpg", ".jpeg", ".gif",
            ".webp", ".svg", ".ico", ".woff", ".woff2", ".ttf", ".map"
        };
        return extensoesEstaticas.Any(ext =>
            caminho.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    private record StatusAssinaturaCache(bool Ativa, DateTime DataExpiracao);
}
using DigDog.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DigDog.Middlewares;

public class AssinaturaMiddleware
{
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
        UserManager<IdentityUser> gerenciadorUsuario)
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

        var vinculo = await db.EmpresaUsuario
            .FirstOrDefaultAsync(eu => eu.IdUsuario == idUsuario);

        if (vinculo == null)
        {
            contexto.Response.Redirect("/Assinatura/Inativa");
            return;
        }

        var assinatura = await db.Assinatura
            .Where(a => a.IdEmpresa == vinculo.IdEmpresa)
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync();

        var ativa = assinatura != null
                    && assinatura.Ativa
                    && assinatura.DataExpiracao >= DateTime.Now;

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
}
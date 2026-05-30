using DigDog.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DigDog.Middlewares;

public class AssinaturaMiddleware
{
    private readonly RequestDelegate _proximo;

    // Claim armazenada no cookie: "1" = ativa, "0" = inativa
    // Revalidada a cada 30 minutos no banco, evitando uma query por requisição.
    private const string ClaimAssinatura       = "AssinaturaAtiva";
    private const string ClaimAssinaturaExpira = "AssinaturaExpiraEm";
    private static readonly TimeSpan JanelaCacheAssinatura = TimeSpan.FromMinutes(30);

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
        IAuthenticationService autenticacao)
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

        var assinaturaAtiva = await ObterStatusAssinaturaAsync(
            contexto, db, gerenciadorUsuario, autenticacao);

        if (!assinaturaAtiva)
        {
            contexto.Response.Redirect("/Assinatura/Inativa");
            return;
        }

        await _proximo(contexto);
    }

    // ── Lógica de cache ────────────────────────────────────────────────────

    private static async Task<bool> ObterStatusAssinaturaAsync(
        HttpContext contexto,
        Contexto db,
        UserManager<IdentityUser> gerenciadorUsuario,
        IAuthenticationService autenticacao)
    {
        var principal = contexto.User;

        // Lê o cache da claim — se presente e não expirado, sem query ao banco
        var claimAtiva  = principal.FindFirstValue(ClaimAssinatura);
        var claimExpira = principal.FindFirstValue(ClaimAssinaturaExpira);

        if (claimAtiva != null && claimExpira != null
            && DateTime.TryParse(claimExpira, out var expira)
            && DateTime.UtcNow < expira)
        {
            return claimAtiva == "1";
        }

        // Cache ausente ou expirado → consulta o banco (1x a cada 30 min por usuário)
        var idUsuario = gerenciadorUsuario.GetUserId(principal);
        var vinculo   = await db.EmpresaUsuario
            .FirstOrDefaultAsync(eu => eu.IdUsuario == idUsuario);

        bool ativa = false;
        if (vinculo != null)
        {
            var assinatura = await db.Assinatura
                .Where(a => a.IdEmpresa == vinculo.IdEmpresa)
                .OrderByDescending(a => a.Id)
                .FirstOrDefaultAsync();

            ativa = assinatura != null
                    && assinatura.Ativa
                    && assinatura.DataExpiracao >= DateTime.Now;
        }

        // Persiste o resultado no cookie para os próximos 30 min
        await AtualizarClaimCookieAsync(contexto, autenticacao, ativa);

        return ativa;
    }

    private static async Task AtualizarClaimCookieAsync(
        HttpContext contexto,
        IAuthenticationService autenticacao,
        bool ativa)
    {
        try
        {
            var identidade = contexto.User.Identity as ClaimsIdentity;
            if (identidade == null) return;

            // Remove claims antigas antes de adicionar as novas
            var claimsRemover = identidade.Claims
                .Where(c => c.Type == ClaimAssinatura || c.Type == ClaimAssinaturaExpira)
                .ToList();
            foreach (var c in claimsRemover)
                identidade.RemoveClaim(c);

            identidade.AddClaim(new Claim(ClaimAssinatura, ativa ? "1" : "0"));
            identidade.AddClaim(new Claim(
                ClaimAssinaturaExpira,
                DateTime.UtcNow.Add(JanelaCacheAssinatura).ToString("O")));

            // Re-emite o cookie com as claims atualizadas
            await autenticacao.SignInAsync(
                contexto,
                IdentityConstants.ApplicationScheme,
                contexto.User,
                new AuthenticationProperties { IsPersistent = true });
        }
        catch
        {
            // Falha silenciosa — na pior hipótese, a próxima req volta ao banco
        }
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

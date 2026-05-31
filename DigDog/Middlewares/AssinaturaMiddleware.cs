using DigDog.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DigDog.Middlewares;

public class AssinaturaMiddleware
{
    private readonly RequestDelegate _proximo;

    private const string ClaimAssinatura           = "AssinaturaAtiva";
    private const string ClaimAssinaturaExpira     = "AssinaturaExpiraEm";
    private const string ClaimAssinaturaVencimento = "AssinaturaVencimentoEm";
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

    private static async Task<bool> ObterStatusAssinaturaAsync(
        HttpContext contexto,
        Contexto db,
        UserManager<IdentityUser> gerenciadorUsuario,
        IAuthenticationService autenticacao)
    {
        var principal = contexto.User;

        var claimAtiva      = principal.FindFirstValue(ClaimAssinatura);
        var claimExpira     = principal.FindFirstValue(ClaimAssinaturaExpira);
        var claimVencimento = principal.FindFirstValue(ClaimAssinaturaVencimento);

        if (claimAtiva != null && claimExpira != null
            && DateTime.TryParse(claimExpira, out var expira)
            && DateTime.UtcNow < expira)
        {
            // Cache ainda válido — mas verifica se a assinatura já venceu desde que foi cacheada
            if (claimAtiva == "1"
                && claimVencimento != null
                && DateTime.TryParse(claimVencimento, out var vencimento)
                && DateTime.Now > vencimento)
            {
                // Assinatura venceu — ignora o cache e cai na consulta ao banco abaixo
            }
            else
            {
                return claimAtiva == "1";
            }
        }

        // Cache ausente, expirado ou assinatura vencida → consulta o banco
        var idUsuario = gerenciadorUsuario.GetUserId(principal);
        var vinculo   = await db.EmpresaUsuario
            .FirstOrDefaultAsync(eu => eu.IdUsuario == idUsuario);

        bool ativa                  = false;
        DateTime? vencimentoReal    = null;

        if (vinculo != null)
        {
            var assinatura = await db.Assinatura
                .Where(a => a.IdEmpresa == vinculo.IdEmpresa)
                .OrderByDescending(a => a.Id)
                .FirstOrDefaultAsync();

            if (assinatura != null)
            {
                vencimentoReal = assinatura.DataExpiracao;
                ativa = assinatura.Ativa && assinatura.DataExpiracao >= DateTime.Now;
            }
        }

        await AtualizarClaimCookieAsync(contexto, autenticacao, ativa, vencimentoReal);

        return ativa;
    }

    private static async Task AtualizarClaimCookieAsync(
        HttpContext contexto,
        IAuthenticationService autenticacao,
        bool ativa,
        DateTime? vencimentoAssinatura = null)
    {
        try
        {
            var identidade = contexto.User.Identity as ClaimsIdentity;
            if (identidade == null) return;

            // Remove claims antigas antes de adicionar as novas
            var claimsRemover = identidade.Claims
                .Where(c => c.Type == ClaimAssinatura
                         || c.Type == ClaimAssinaturaExpira
                         || c.Type == ClaimAssinaturaVencimento)
                .ToList();
            foreach (var c in claimsRemover)
                identidade.RemoveClaim(c);

            identidade.AddClaim(new Claim(ClaimAssinatura, ativa ? "1" : "0"));
            identidade.AddClaim(new Claim(
                ClaimAssinaturaExpira,
                DateTime.UtcNow.Add(JanelaCacheAssinatura).ToString("O")));

            // Persiste o vencimento real da assinatura para verificação durante o cache
            if (vencimentoAssinatura.HasValue)
                identidade.AddClaim(new Claim(
                    ClaimAssinaturaVencimento,
                    vencimentoAssinatura.Value.ToString("O")));

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
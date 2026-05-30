using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigDog.Data;
using DigDog.Hubs;
using DigDog.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DigDog.Services;

public class CaktoWebhookService
{
    private readonly Contexto _contexto;
    private readonly UserManager<IdentityUser> _gerenciadorUsuario;
    private readonly IConfiguration _configuracao;
    private readonly ILogger<CaktoWebhookService> _logger;
    private readonly IHubContext<AssinaturaHub> _hubAssinatura;

    public CaktoWebhookService(
        Contexto contexto,
        UserManager<IdentityUser> gerenciadorUsuario,
        IConfiguration configuracao,
        ILogger<CaktoWebhookService> logger,
        IHubContext<AssinaturaHub> hubAssinatura)
    {
        _contexto           = contexto;
        _gerenciadorUsuario = gerenciadorUsuario;
        _configuracao       = configuracao;
        _logger             = logger;
        _hubAssinatura      = hubAssinatura;
    }

    public bool AssinaturaValida(IHeaderDictionary headers, string bodyBruto)
    {
        var chaveSecreta = _configuracao["Cakto:ChaveSecreta"] ?? "";

        // Cakto envia o secret dentro do body JSON
        try
        {
            var doc    = JsonDocument.Parse(bodyBruto);
            var secret = ObterString(doc.RootElement, "secret");

            if (!string.IsNullOrWhiteSpace(secret))
                return secret == chaveSecreta;
        }
        catch { }

        // Fallback: validação por header HMAC
        var assinaturaHeader = headers["X-Cakto-Signature"].FirstOrDefault()
                            ?? headers["X-Webhook-Signature"].FirstOrDefault()
                            ?? headers["X-Signature"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(assinaturaHeader))
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }

        using var hmac    = new HMACSHA256(Encoding.UTF8.GetBytes(chaveSecreta));
        var hashBytes     = hmac.ComputeHash(Encoding.UTF8.GetBytes(bodyBruto));
        var hashCalculado = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var assinaturaLimpa = assinaturaHeader.StartsWith("sha256=")
            ? assinaturaHeader[7..]
            : assinaturaHeader;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(hashCalculado),
            Encoding.UTF8.GetBytes(assinaturaLimpa.ToLowerInvariant()));
    }

    public async Task ProcessarEventoAsync(CaktoPayloadViewModel payload)
    {
        var idEmpresa = await LocalizarEmpresaAsync(payload.Email, payload.Cpf);

        if (idEmpresa == null)
        {
            _logger.LogWarning(
                "Webhook Cakto: nenhum usuário encontrado para email={Email} cpf={Cpf}",
                payload.Email, payload.Cpf);
            return;
        }

        switch (payload.Evento.ToLowerInvariant())
        {
            case "purchase_approved":
            case "subscription_created":
            case "subscription_renewed":
                await AtivarOuRenovarAsync(idEmpresa.Value, meses: 1);
                break;

            case "subscription_canceled":
            case "refund":
            case "chargeback":
                await DesativarAsync(idEmpresa.Value);
                break;

            default:
                _logger.LogInformation("Webhook Cakto: evento '{Evento}' ignorado.", payload.Evento);
                break;
        }
    }

    public CaktoPayloadViewModel ExtrairPayload(string bodyBruto)
    {
        var doc  = JsonDocument.Parse(bodyBruto);
        var raiz = doc.RootElement;

        return new CaktoPayloadViewModel
        {
            Evento = ObterString(raiz, "event") ?? ObterString(raiz, "type") ?? "",
            Email  = ExtrairEmail(raiz),
            Cpf    = ExtrairCpf(raiz)
        };
    }

    // ── Privados ───────────────────────────────────────────────────────────

    private async Task AtivarOuRenovarAsync(int idEmpresa, int meses)
    {
        var assinatura = await _contexto.Assinatura
            .Where(a => a.IdEmpresa == idEmpresa)
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync();

        var agora = DateTime.Now;

        if (assinatura == null)
        {
            _contexto.Assinatura.Add(new Assinatura
            {
                IdEmpresa     = idEmpresa,
                DataInicio    = agora,
                DataExpiracao = agora.AddMonths(meses),
                Ativa         = true
            });
        }
        else
        {
            var baseData             = assinatura.DataExpiracao < agora ? agora : assinatura.DataExpiracao;
            assinatura.DataExpiracao = baseData.AddMonths(meses);
            assinatura.Ativa         = true;
        }

        await _contexto.SaveChangesAsync();
        _logger.LogInformation("Assinatura ativada/renovada para empresa {IdEmpresa}.", idEmpresa);
        await _hubAssinatura.Clients
            .Group($"empresa-{idEmpresa}")
            .SendAsync("AssinaturaAtivada");
    }

    private async Task DesativarAsync(int idEmpresa)
    {
        var assinatura = await _contexto.Assinatura
            .Where(a => a.IdEmpresa == idEmpresa)
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync();

        if (assinatura == null) return;

        assinatura.Ativa = false;
        await _contexto.SaveChangesAsync();
        _logger.LogInformation("Assinatura desativada para empresa {IdEmpresa}.", idEmpresa);
    }

    /// <summary>
    /// Localiza a empresa pelo e-mail ou CPF do comprador.
    ///
    /// Busca por e-mail usa o índice do Identity (FindByEmailAsync) — O(1).
    ///
    /// Busca por CPF: o CPF é armazenado como claim "Cpf" na tabela
    /// AspNetUserClaims. Em vez de carregar todos os usuários em memória
    /// (.Users.ToList()), fazemos a query diretamente na tabela de claims
    /// via _contexto, que é indexada e muito mais eficiente.
    /// </summary>
    private async Task<int?> LocalizarEmpresaAsync(string? email, string? cpf)
    {
        // ── 1. Busca por e-mail ────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(email))
        {
            var usuarioPorEmail = await _gerenciadorUsuario.FindByEmailAsync(email);
            if (usuarioPorEmail != null)
            {
                var vinculo = await _contexto.EmpresaUsuario
                    .FirstOrDefaultAsync(eu => eu.IdUsuario == usuarioPorEmail.Id);
                if (vinculo != null) return vinculo.IdEmpresa;
            }
        }

        // ── 2. Busca por CPF via tabela de claims (sem carregar tudo em memória) ──
        if (!string.IsNullOrWhiteSpace(cpf))
        {
            var cpfLimpo = new string(cpf.Where(char.IsDigit).ToArray());

            if (!string.IsNullOrWhiteSpace(cpfLimpo))
            {
                // AspNetUserClaims é a tabela gerada pelo Identity para claims.
                // A query é traduzida para SQL e executada no banco — não em memória.
                var idUsuarioPorCpf = await _contexto.UserClaims
                    .Where(uc => uc.ClaimType == "Cpf" && uc.ClaimValue == cpfLimpo)
                    .Select(uc => uc.UserId)
                    .FirstOrDefaultAsync();

                if (idUsuarioPorCpf != null)
                {
                    var vinculo = await _contexto.EmpresaUsuario
                        .FirstOrDefaultAsync(eu => eu.IdUsuario == idUsuarioPorCpf);
                    if (vinculo != null) return vinculo.IdEmpresa;
                }
            }
        }

        return null;
    }

    private static string? ExtrairEmail(JsonElement raiz)
    {
        var caminhos = new[]
        {
            "data.customer.email",
            "customer.email",
            "email"
        };
        return caminhos.Select(c => NavegaJson(raiz, c)).FirstOrDefault(v => v != null);
    }

    private static string? ExtrairCpf(JsonElement raiz)
    {
        var caminhos = new[]
        {
            "data.customer.docNumber",
            "data.customer.document",
            "customer.docNumber",
            "customer.document",
            "document"
        };
        return caminhos.Select(c => NavegaJson(raiz, c)).FirstOrDefault(v => v != null);
    }

    private static string? NavegaJson(JsonElement elemento, string caminho)
    {
        var partes = caminho.Split('.');
        var atual  = elemento;

        foreach (var parte in partes)
        {
            if (!atual.TryGetProperty(parte, out atual))
                return null;
        }

        return atual.ValueKind == JsonValueKind.String ? atual.GetString() : null;
    }

    private static string? ObterString(JsonElement elemento, string chave)
    {
        if (elemento.TryGetProperty(chave, out var val) && val.ValueKind == JsonValueKind.String)
            return val.GetString();
        return null;
    }
}

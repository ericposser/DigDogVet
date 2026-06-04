using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigDog.Data;
using DigDog.Hubs;
using DigDog.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DigDog.Services;

public class CaktoWebhookService
{
    private readonly Contexto _contexto;
    private readonly UserManager<IdentityUser> _gerenciadorUsuario;
    private readonly IConfiguration _configuracao;
    private readonly ILogger<CaktoWebhookService> _logger;
    private readonly IHubContext<AssinaturaHub> _hubAssinatura;
    private readonly IMemoryCache _cache;

    public CaktoWebhookService(
        Contexto contexto,
        UserManager<IdentityUser> gerenciadorUsuario,
        IConfiguration configuracao,
        ILogger<CaktoWebhookService> logger,
        IHubContext<AssinaturaHub> hubAssinatura,
        IMemoryCache cache)
    {
        _contexto           = contexto;
        _gerenciadorUsuario = gerenciadorUsuario;
        _configuracao       = configuracao;
        _logger             = logger;
        _hubAssinatura      = hubAssinatura;
        _cache              = cache;
    }

    public bool AssinaturaValida(IHeaderDictionary headers, string bodyBruto)
    {
        // ... (sem mudanças) ...
        var chaveSecreta = _configuracao["Cakto:ChaveSecreta"] ?? "";

        try
        {
            var doc    = JsonDocument.Parse(bodyBruto);
            var secret = ObterString(doc.RootElement, "secret");
            if (!string.IsNullOrWhiteSpace(secret))
                return secret == chaveSecreta;
        }
        catch { }

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

        // ─── INVALIDA o cache do middleware ───────────────────────────────
        _cache.Remove($"assinatura-status:{idEmpresa}");

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

        // ─── INVALIDA o cache do middleware ───────────────────────────────
        _cache.Remove($"assinatura-status:{idEmpresa}");

        _logger.LogInformation("Assinatura desativada para empresa {IdEmpresa}.", idEmpresa);
    }

    private async Task<int?> LocalizarEmpresaAsync(string? email, string? cpf)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            var usuarioPorEmail = await _gerenciadorUsuario.FindByEmailAsync(email);
            if (usuarioPorEmail != null)
            {
                var vinculo = await _contexto.EmpresaUsuario
                    .AsNoTracking()
                    .FirstOrDefaultAsync(eu => eu.IdUsuario == usuarioPorEmail.Id);
                if (vinculo != null) return vinculo.IdEmpresa;
            }
        }

        if (!string.IsNullOrWhiteSpace(cpf))
        {
            var cpfLimpo = new string(cpf.Where(char.IsDigit).ToArray());

            if (!string.IsNullOrWhiteSpace(cpfLimpo))
            {
                var idUsuarioPorCpf = await _contexto.UserClaims
                    .AsNoTracking()
                    .Where(uc => uc.ClaimType == "Cpf" && uc.ClaimValue == cpfLimpo)
                    .Select(uc => uc.UserId)
                    .FirstOrDefaultAsync();

                if (idUsuarioPorCpf != null)
                {
                    var vinculo = await _contexto.EmpresaUsuario
                        .AsNoTracking()
                        .FirstOrDefaultAsync(eu => eu.IdUsuario == idUsuarioPorCpf);
                    if (vinculo != null) return vinculo.IdEmpresa;
                }
            }
        }

        return null;
    }

    // ... métodos auxiliares JSON sem mudanças ...

    private static string? ExtrairEmail(JsonElement raiz)
    {
        var caminhos = new[] { "data.customer.email", "customer.email", "email" };
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
            if (!atual.TryGetProperty(parte, out atual)) return null;
        return atual.ValueKind == JsonValueKind.String ? atual.GetString() : null;
    }

    private static string? ObterString(JsonElement elemento, string chave)
    {
        if (elemento.TryGetProperty(chave, out var val) && val.ValueKind == JsonValueKind.String)
            return val.GetString();
        return null;
    }
}
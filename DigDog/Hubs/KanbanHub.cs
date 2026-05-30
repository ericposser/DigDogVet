using DigDog.Data;
using DigDog.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace DigDog.Hubs;

public class KanbanHub : Hub
{
    private readonly Contexto _contexto;

    public KanbanHub(Contexto contexto)
    {
        _contexto = contexto;
    }

    // ── Entrar na sala do agendamento ──────────────────────────────────────
    public async Task EntrarNaSala(string token)
    {
        // Valida o formato do token antes de qualquer hash ou query ao banco.
        // Um GUID padrão tem 36 caracteres; aceitamos até 128 como margem.
        // Aceita apenas hex + hífens (formato GUID).
        if (!TokenValido(token))
        {
            await Clients.Caller.SendAsync("ErroDeConexao", "Token inválido.");
            return;
        }

        var hash = GerarHash(token);

        var kanbanToken = await _contexto.KanbanToken
            .FirstOrDefaultAsync(t =>
                t.TokenHash == hash &&
                t.Ativo     &&
                t.ExpiraEm  > DateTime.UtcNow);

        if (kanbanToken == null)
        {
            await Clients.Caller.SendAsync("ErroDeConexao", "Token inválido ou expirado.");
            return;
        }

        var nomeGrupo = NomeGrupo(hash);
        await Groups.AddToGroupAsync(Context.ConnectionId, nomeGrupo);

        Context.Items["TokenHash"]   = hash;
        Context.Items["IdEmpresa"]   = kanbanToken.IdEmpresa;
        Context.Items["IdBanhoTosa"] = kanbanToken.IdBanhoTosa;
    }

    // ── Mover card (somente usuário da empresa pode chamar) ────────────────
    public async Task MoverCard(int idBanhoTosa, int novoStatus)
    {
        if (!Context.Items.TryGetValue("TokenHash",   out var hashObj)    ||
            !Context.Items.TryGetValue("IdEmpresa",   out var empresaObj) ||
            !Context.Items.TryGetValue("IdBanhoTosa", out var banhoObj))
        {
            await Clients.Caller.SendAsync("ErroDeConexao", "Não autorizado.");
            return;
        }

        var hash        = hashObj!.ToString()!;
        var idEmpresa   = (int)empresaObj!;
        var idBanhoSala = (int)banhoObj!;

        if (idBanhoTosa != idBanhoSala)
        {
            await Clients.Caller.SendAsync("ErroDeConexao", "Operação não permitida.");
            return;
        }

        var idUsuarioConectado = Context.User?
            .FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (idUsuarioConectado == null)
        {
            await Clients.Caller.SendAsync("ErroDeConexao", "Sem permissão para mover este card.");
            return;
        }

        var pertenceAEmpresa = await _contexto.EmpresaUsuario
            .AnyAsync(eu => eu.IdUsuario == idUsuarioConectado
                         && eu.IdEmpresa == idEmpresa);

        if (!pertenceAEmpresa)
        {
            await Clients.Caller.SendAsync("ErroDeConexao", "Sem permissão para mover este card.");
            return;
        }

        if (!Enum.IsDefined(typeof(StatusKanban), novoStatus))
        {
            await Clients.Caller.SendAsync("ErroDeConexao", "Status inválido.");
            return;
        }

        var banho = await _contexto.BanhoTosa
            .Where(b => b.Id == idBanhoTosa && b.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync();

        if (banho == null)
        {
            await Clients.Caller.SendAsync("ErroDeConexao", "Agendamento não encontrado.");
            return;
        }

        banho.StatusKanban = (StatusKanban)novoStatus;
        await _contexto.SaveChangesAsync();

        await Clients.Group(NomeGrupo(hash)).SendAsync("CardMovido", idBanhoTosa, novoStatus);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string NomeGrupo(string hash) => $"kanban-{hash}";

    public static string GerarHash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Valida que o token tem formato mínimo aceitável antes de processar.
    /// Aceita apenas caracteres de GUID (hex + hífens), máximo 128 chars.
    /// </summary>
    private static bool TokenValido(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (token.Length > 128)               return false;

        foreach (var c in token)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-') return false;
        }
        return true;
    }
}

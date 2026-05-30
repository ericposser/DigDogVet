using DigDog.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DigDog.Hubs;

public class AssinaturaHub : Hub
{
    private readonly Contexto _contexto;

    public AssinaturaHub(Contexto contexto)
    {
        _contexto = contexto;
    }

    // ── Entrar no grupo da empresa ─────────────────────────────────────────
    // O cliente envia o idEmpresa que deseja monitorar.
    // Antes de adicionar ao grupo, verificamos que o usuário autenticado
    // realmente pertence àquela empresa — impede que um cliente forje o ID
    // e entre no grupo de outra empresa.
    [Authorize]
    public async Task EntrarGrupo(string idEmpresa)
    {
        // Valida que o valor recebido é um inteiro positivo
        if (!int.TryParse(idEmpresa, out var idEmpresaInt) || idEmpresaInt <= 0)
        {
            await Clients.Caller.SendAsync("ErroDeConexao", "Empresa inválida.");
            return;
        }

        var idUsuario = Context.User?
            .FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (idUsuario == null)
        {
            await Clients.Caller.SendAsync("ErroDeConexao", "Não autenticado.");
            return;
        }

        // Confirma que o usuário autenticado pertence à empresa solicitada
        var pertence = await _contexto.EmpresaUsuario
            .AnyAsync(eu => eu.IdUsuario == idUsuario && eu.IdEmpresa == idEmpresaInt);

        if (!pertence)
        {
            await Clients.Caller.SendAsync("ErroDeConexao", "Sem permissão para este grupo.");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"empresa-{idEmpresaInt}");
    }
}
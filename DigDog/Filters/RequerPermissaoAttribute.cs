using DigDog.Models;
using DigDog.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace DigDog.Filters;

/// <summary>
/// Decorator para proteger actions. Uso: [RequerPermissao(Permissao.PetsVisualizar)]
/// </summary>
public class RequerPermissaoAttribute : TypeFilterAttribute
{
    public RequerPermissaoAttribute(Permissao permissao)
        : base(typeof(RequerPermissaoFiltro))
    {
        Arguments = new object[] { permissao };
    }
}

public class RequerPermissaoFiltro : IAsyncAuthorizationFilter
{
    private readonly PermissaoService _permissaoService;
    private readonly Permissao _permissao;

    public RequerPermissaoFiltro(PermissaoService permissaoService, Permissao permissao)
    {
        _permissaoService = permissaoService;
        _permissao = permissao;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext contexto)
    {
        var usuario = contexto.HttpContext.User;

        // Não autenticado → redireciona para login
        if (usuario?.Identity?.IsAuthenticated != true)
        {
            contexto.Result = new ChallengeResult();
            return;
        }

        // Admin passa direto
        if (usuario.IsInRole("Admin"))
            return;

        var idUsuario = usuario.FindFirstValue(ClaimTypes.NameIdentifier);

        if (idUsuario == null ||
            !await _permissaoService.UsuarioTemPermissaoAsync(idUsuario, _permissao))
        {
            // Sem permissão → página de acesso negado
            contexto.Result = new ForbidResult();
        }
    }
}
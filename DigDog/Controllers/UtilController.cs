using DigDog.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DigDog.Controllers;

public class UtilController : Controller
{
    protected readonly UserManager<IdentityUser> _gerenciadorUsuario;
    protected readonly Contexto _contextoBase;
    protected readonly IMemoryCache _cacheBase;
    private readonly IDataProtector _protetor;

    // TTLs do cache
    private static readonly TimeSpan TtlIdEmpresa       = TimeSpan.FromHours(1);
    private static readonly TimeSpan TtlInfoUsuario     = TimeSpan.FromMinutes(10);

    public UtilController(
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao,
        Contexto contexto,
        IMemoryCache cache)
    {
        _gerenciadorUsuario = gerenciadorUsuario;
        _protetor           = provedorProtecao.CreateProtector("DigDog.IdProtecao");
        _contextoBase       = contexto;
        _cacheBase          = cache;
    }

    // ── Identidade do usuário ──────────────────────────────────────────────

    protected string ObterIdUsuario()
    {
        return _gerenciadorUsuario.GetUserId(User)
               ?? throw new InvalidOperationException("Usuário não autenticado.");
    }

    /// <summary>
    /// Retorna o IdEmpresa do usuário logado.
    /// Cacheado por 1 hora — o vínculo Empresa↔Usuário praticamente não muda.
    /// Quando o usuário é removido, a chave expira naturalmente; ele não consegue mais logar.
    /// </summary>
    protected async Task<int> ObterIdEmpresaAsync()
    {
        var idUsuario  = ObterIdUsuario();
        var chaveCache = $"idEmpresa:{idUsuario}";

        if (_cacheBase.TryGetValue<int>(chaveCache, out var idEmpresaCache))
            return idEmpresaCache;

        // Projeção: não carrega entidade inteira, só o IdEmpresa
        var idEmpresa = await _contextoBase.EmpresaUsuario
            .AsNoTracking()
            .Where(eu => eu.IdUsuario == idUsuario)
            .Select(eu => (int?)eu.IdEmpresa)
            .FirstOrDefaultAsync();

        if (idEmpresa == null)
            throw new InvalidOperationException("Usuário não está vinculado a nenhuma empresa.");

        _cacheBase.Set(chaveCache, idEmpresa.Value, TtlIdEmpresa);
        return idEmpresa.Value;
    }

    [HttpGet]
    [Microsoft.AspNetCore.Authorization.Authorize]
    [EnableRateLimiting("geral")]
    public async Task<IActionResult> ObterIdEmpresa()
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        return Json(new { idEmpresa });
    }

    /// <summary>
    /// Retorna o email (UserName) do usuário logado. Cacheado por 10 minutos.
    /// </summary>
    protected async Task<string> ObterEmailUsuario()
    {
        var idUsuario  = ObterIdUsuario();
        var chaveCache = $"emailUsuario:{idUsuario}";

        if (_cacheBase.TryGetValue<string>(chaveCache, out var emailCache) && emailCache != null)
            return emailCache;

        // Projeção direta na tabela em vez de UserManager.FindByIdAsync
        var email = await _contextoBase.Users
            .AsNoTracking()
            .Where(u => u.Id == idUsuario)
            .Select(u => u.UserName)
            .FirstOrDefaultAsync()
            ?? "Não identificado";

        _cacheBase.Set(chaveCache, email, TtlInfoUsuario);
        return email;
    }

    /// <summary>
    /// Retorna o NomeFuncionario (claim) ou o email como fallback. Cacheado por 10 minutos.
    /// Quando o Admin edita um funcionário, a chave deve ser invalidada manualmente
    /// (ver UsuarioController.Edit).
    /// </summary>
    protected async Task<string> ObterNomeFuncionarioAsync()
    {
        var idUsuario  = ObterIdUsuario();
        var chaveCache = $"nomeFuncionario:{idUsuario}";

        if (_cacheBase.TryGetValue<string>(chaveCache, out var nomeCache) && nomeCache != null)
            return nomeCache;

        // Uma única query — JOIN entre Users e UserClaims com projeção
        var resultado = await (
            from u in _contextoBase.Users.AsNoTracking()
            where u.Id == idUsuario
            select new
            {
                Email = u.UserName,
                Nome  = _contextoBase.UserClaims
                    .Where(c => c.UserId == idUsuario && c.ClaimType == "NomeFuncionario")
                    .Select(c => c.ClaimValue)
                    .FirstOrDefault()
            }
        ).FirstOrDefaultAsync();

        var nomeFinal = !string.IsNullOrWhiteSpace(resultado?.Nome)
            ? resultado.Nome
            : resultado?.Email ?? "Desconhecido";

        _cacheBase.Set(chaveCache, nomeFinal, TtlInfoUsuario);
        return nomeFinal;
    }

    /// <summary>
    /// Invalida os caches de informações do usuário.
    /// Use quando atualizar email, telefone ou claim NomeFuncionario de um funcionário.
    /// </summary>
    protected void InvalidarCacheUsuario(string idUsuario)
    {
        _cacheBase.Remove($"emailUsuario:{idUsuario}");
        _cacheBase.Remove($"nomeFuncionario:{idUsuario}");
    }

    /// <summary>
    /// Invalida o cache de vínculo Empresa↔Usuário.
    /// Use ao remover um funcionário.
    /// </summary>
    protected void InvalidarCacheIdEmpresa(string idUsuario)
    {
        _cacheBase.Remove($"idEmpresa:{idUsuario}");
    }

    // ── Limpeza de ModelState ──────────────────────────────────────────────

    protected void RemoverValidacaoEmpresa() => ModelState.Remove("IdEmpresa");

    protected void RemoverValidacaoUsuario()
    {
        ModelState.Remove("IdUsuario");
        ModelState.Remove("IdEmpresa");
        ModelState.Remove("Empresa");
    }

    // ── Feedback visual ────────────────────────────────────────────────────

    protected void DefinirToast(string mensagem, string tipo = "primary")
    {
        TempData["ToastMensagem"] = mensagem;
        TempData["ToastTipo"]     = tipo;
    }

    // ── Criptografia de IDs nas URLs ───────────────────────────────────────

    protected string CriptografarId(int id)
    {
        var protegido = _protetor.Protect(id.ToString());
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(protegido))
            .Replace('+', '-').Replace('/', '_').Replace("=", "");
    }

    protected int? DescriptografarId(string idCriptografado)
    {
        try
        {
            var base64  = idCriptografado.Replace('-', '+').Replace('_', '/');
            var padding = base64.Length % 4;
            if (padding > 0) base64 += new string('=', 4 - padding);
            var protegido = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var idTexto   = _protetor.Unprotect(protegido);
            return int.Parse(idTexto);
        }
        catch
        {
            return null;
        }
    }

    public override void OnActionExecuted(ActionExecutedContext context)
    {
        base.OnActionExecuted(context);
        ViewBag.CriptografarId = (Func<int, string>)(id => CriptografarId(id));
    }

    // ── Helpers de segurança reutilizáveis ─────────────────────────────────

    protected bool ValidarTamanhoTexto(string nomeCampo, string? valor, int tamanhoMaximo = 2000)
    {
        if (valor != null && valor.Length > tamanhoMaximo)
        {
            ModelState.AddModelError(nomeCampo,
                $"O campo não pode exceder {tamanhoMaximo} caracteres.");
            return false;
        }
        return true;
    }

    protected bool TokenPublicoValido(string? token)
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
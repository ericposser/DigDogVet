using DigDog.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DigDog.Controllers;

public class UtilController : Controller
{
    protected readonly UserManager<IdentityUser> _gerenciadorUsuario;
    protected readonly Contexto _contextoBase;
    private readonly IDataProtector _protetor;

    public UtilController(
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao,
        Contexto contexto)
    {
        _gerenciadorUsuario = gerenciadorUsuario;
        _protetor           = provedorProtecao.CreateProtector("DigDog.IdProtecao");
        _contextoBase       = contexto;
    }

    // ── Identidade do usuário ──────────────────────────────────────────────

    protected string ObterIdUsuario()
    {
        return _gerenciadorUsuario.GetUserId(User)
               ?? throw new InvalidOperationException("Usuário não autenticado.");
    }

    protected async Task<int> ObterIdEmpresaAsync()
    {
        var idUsuario = ObterIdUsuario();
        var vinculo   = await _contextoBase.EmpresaUsuario
            .FirstOrDefaultAsync(eu => eu.IdUsuario == idUsuario);

        if (vinculo == null)
            throw new InvalidOperationException("Usuário não está vinculado a nenhuma empresa.");

        return vinculo.IdEmpresa;
    }

    // ── Endpoint JSON exposto via GET ──────────────────────────────────────
    // Requer autenticação explícita e rate limit "geral".
    // Usado pelo frontend para obter o idEmpresa via fetch.
    [HttpGet]
    [Microsoft.AspNetCore.Authorization.Authorize]
    [EnableRateLimiting("geral")]
    public async Task<IActionResult> ObterIdEmpresa()
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        return Json(new { idEmpresa });
    }

    protected async Task<string> ObterEmailUsuario()
    {
        var id      = ObterIdUsuario();
        var usuario = await _gerenciadorUsuario.FindByIdAsync(id);
        return usuario?.UserName ?? "Não identificado";
    }

    // ── Limpeza de ModelState ──────────────────────────────────────────────

    protected void RemoverValidacaoEmpresa()
    {
        ModelState.Remove("IdEmpresa");
    }

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

    // ── ViewBag para as views ──────────────────────────────────────────────

    public override void OnActionExecuted(ActionExecutedContext context)
    {
        base.OnActionExecuted(context);
        ViewBag.CriptografarId = (Func<int, string>)(id => CriptografarId(id));
    }

    // ── Informações do funcionário ─────────────────────────────────────────

    protected async Task<string> ObterNomeFuncionarioAsync()
    {
        var idUsuario = ObterIdUsuario();
        var usuario   = await _gerenciadorUsuario.FindByIdAsync(idUsuario);
        if (usuario == null) return "Desconhecido";

        var claims = await _gerenciadorUsuario.GetClaimsAsync(usuario);
        var nome   = claims.FirstOrDefault(c => c.Type == "NomeFuncionario")?.Value;

        return !string.IsNullOrWhiteSpace(nome) ? nome : usuario.Email ?? "Desconhecido";
    }

    // ── Helpers de segurança reutilizáveis ─────────────────────────────────

    /// <summary>
    /// Valida se um texto enviado pelo usuário não excede o tamanho máximo permitido.
    /// Deve ser chamado em actions que recebem campos de texto livre (observações, descrições, etc.)
    /// antes de persistir no banco, evitando payloads gigantes.
    /// </summary>
    /// <param name="nomeCampo">Nome do campo para a mensagem de erro no ModelState.</param>
    /// <param name="valor">Valor recebido.</param>
    /// <param name="tamanhoMaximo">Limite de caracteres (padrão: 2000).</param>
    /// <returns>true se válido, false se excede o limite (e adiciona erro ao ModelState).</returns>
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

    /// <summary>
    /// Verifica se um token anônimo recebido em rotas públicas tem formato mínimo válido,
    /// evitando processamento desnecessário (hash SHA-256, consulta ao banco, etc.)
    /// com entradas obviamente inválidas ou muito longas.
    /// Um GUID padrão tem 36 caracteres; aceitamos até 128 como margem.
    /// </summary>
    protected bool TokenPublicoValido(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (token.Length > 128)               return false;

        // Aceita apenas caracteres de GUID (hex + hífens)
        foreach (var c in token)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-') return false;
        }
        return true;
    }
}

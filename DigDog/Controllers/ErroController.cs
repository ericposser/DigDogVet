using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DigDog.Controllers;

public class ErroController : Controller
{
    [Route("/Erro")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Index(int? codigo = null)
    {
        var funcionalidadeErro = HttpContext.Features.Get<IExceptionHandlerFeature>();
        var excecao            = funcionalidadeErro?.Error;

        ViewData["RequestId"] = System.Diagnostics.Activity.Current?.Id
                                ?? HttpContext.TraceIdentifier;
        ViewData["Codigo"]    = codigo;

        if (excecao != null)
        {
            // Sobe até a exceção raiz para a mensagem mais direta
            var excecaoRaiz = excecao;
            while (excecaoRaiz.InnerException != null)
                excecaoRaiz = excecaoRaiz.InnerException;

            // Detalhes técnicos só são expostos para usuários autenticados.
            // Visitantes anônimos (e a rota pública /Carteira/Publico, /StatusDia/Publico)
            // recebem apenas a mensagem genérica, sem vazar stack trace ou tipo da exceção.
            if (User.Identity?.IsAuthenticated == true)
            {
                ViewData["TipoErro"]     = excecao.GetType().FullName;
                ViewData["MensagemErro"] = excecaoRaiz.Message;
                ViewData["DetalheErro"]  = excecao.InnerException != null
                    ? $"{excecao.Message}\n\n→ Causa: {excecaoRaiz.Message}"
                    : excecao.Message;
                ViewData["StackTrace"]   = excecao.StackTrace;
            }
            else
            {
                ViewData["TipoErro"]     = string.Empty;
                ViewData["MensagemErro"] = "Ocorreu um erro inesperado. Por favor, tente novamente mais tarde.";
                ViewData["DetalheErro"]  = string.Empty;
                ViewData["StackTrace"]   = string.Empty;
            }
        }
        else
        {
            // Sem exceção, mostra mensagem amigável de acordo com o código HTTP
            ViewData["TipoErro"]     = string.Empty;
            ViewData["MensagemErro"] = codigo switch
            {
                404 => "A página solicitada não foi encontrada.",
                429 => "Você fez muitas requisições em pouco tempo. Aguarde alguns instantes e tente novamente.",
                403 => "Você não tem permissão para acessar esse recurso.",
                500 => "Ocorreu um erro interno no servidor.",
                _   => "Ocorreu um erro inesperado. Nenhum detalhe adicional está disponível."
            };
            ViewData["DetalheErro"]  = string.Empty;
            ViewData["StackTrace"]   = string.Empty;
        }

        return View("Error");
    }

    public IActionResult AcessoNegado() => View();
}
using DigDog.Data;
using DigDog.Models;
using DigDog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DigDog.Controllers;

[Authorize]
public class TicketController : UtilController
{
    private static readonly HashSet<string> _mimesFotoPermitidos =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/gif",
            "image/webp"
        };

    private const long TamanhoMaximoImagem = 5 * 1024 * 1024; // 5 MB

    private readonly TrelloService _trelloServico;

    public TicketController(
        TrelloService trelloServico,
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao,
        Contexto contexto)
        : base(gerenciadorUsuario, provedorProtecao, contexto)
    {
        _trelloServico = trelloServico;
    }

    [HttpGet]
    public IActionResult Enviar() => View(new TicketViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enviar(TicketViewModel modelo)
    {
        ValidarTamanhoTexto(nameof(modelo.Titulo),    modelo.Titulo,    200);
        ValidarTamanhoTexto(nameof(modelo.Descricao), modelo.Descricao, 2000);

        await ValidarImagemAsync(modelo.Imagem);

        if (!ModelState.IsValid)
            return View(modelo);

        var emailUsuario = await ObterEmailUsuario();
        var idCard       = await _trelloServico.CriarCardAsync(
            modelo.Titulo, modelo.Descricao, modelo.Tipo, emailUsuario);

        if (idCard == null)
        {
            ModelState.AddModelError(string.Empty,
                "Não foi possível criar o card. Verifique as configurações e tente novamente.");
            return View(modelo);
        }

        if (modelo.Imagem != null)
        {
            var imagemAnexada = await _trelloServico.AnexarImagemAsync(idCard, modelo.Imagem);
            if (!imagemAnexada)
            {
                DefinirToast("Ticket enviado, porém não foi possível anexar a imagem.", "warning");
                return RedirectToAction(nameof(Enviar));
            }
        }

        DefinirToast("Ticket enviado com sucesso!", "success");
        return RedirectToAction(nameof(Enviar));
    }

    // ── Validação de imagem ────────────────────────────────────────────────

    private async Task ValidarImagemAsync(IFormFile? imagem)
    {
        if (imagem == null) return;

        if (imagem.Length > TamanhoMaximoImagem)
        {
            ModelState.AddModelError(nameof(TicketViewModel.Imagem),
                "A imagem não pode ultrapassar 5 MB.");
            return;
        }

        if (!_mimesFotoPermitidos.Contains(imagem.ContentType))
        {
            ModelState.AddModelError(nameof(TicketViewModel.Imagem),
                "Formato inválido. Envie uma imagem JPG, PNG, GIF ou WEBP.");
            return;
        }

        // Verifica magic bytes para garantir que o conteúdo bate com o MIME declarado
        if (!await ValidarMagicBytesImagemAsync(imagem))
        {
            ModelState.AddModelError(nameof(TicketViewModel.Imagem),
                "O arquivo enviado não é uma imagem válida.");
        }
    }

    /// <summary>
    /// Lê os primeiros bytes do arquivo e confirma que correspondem
    /// ao tipo de imagem declarado no Content-Type.
    /// Suporta: JPEG, PNG, GIF (87a e 89a), WebP.
    /// </summary>
    private static async Task<bool> ValidarMagicBytesImagemAsync(IFormFile imagem)
    {
        var buffer = new byte[12];
        await using var stream = imagem.OpenReadStream();
        var lidos = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));
        if (lidos < 4) return false;

        var mime = imagem.ContentType.ToLowerInvariant();

        // JPEG: FF D8 FF
        if (buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF)
            return mime == "image/jpeg";

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47)
            return mime == "image/png";

        // GIF: GIF87a ou GIF89a
        if (buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46)
            return mime == "image/gif";

        // WebP: RIFF (bytes 0-3) + WEBP (bytes 8-11)
        if (lidos >= 12 &&
            buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46 &&
            buffer[8] == 0x57 && buffer[9] == 0x45 && buffer[10] == 0x42 && buffer[11] == 0x50)
            return mime == "image/webp";

        return false;
    }
}

using DigDog.Data;
using DigDog.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DigDog.Controllers;

[Authorize]
public class ConfiguracaoController : UtilController
{
    private static readonly HashSet<string> _mimesFotoPermitidos =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    private const long TamanhoMaximoFotoBytes = 1024 * 1024;

    private readonly Contexto _contexto;
    private readonly UserManager<IdentityUser> _gerenciadorUsuarioLocal;
    private readonly IMemoryCache _cache;

    public ConfiguracaoController(
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao,
        Contexto contexto,
        IMemoryCache cache)
        : base(gerenciadorUsuario, provedorProtecao, contexto, cache)
    {
        _contexto                = contexto;
        _gerenciadorUsuarioLocal = gerenciadorUsuario;
        _cache                   = cache;
    }

    public async Task<IActionResult> Index()
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        var config    = await _contexto.Configuracao
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.IdEmpresa == idEmpresa)
            ?? new Configuracao { NomeEstabelecimento = "DigDogVet" };

        var usuarioIdentity  = await _gerenciadorUsuarioLocal.GetUserAsync(User);
        ViewBag.EmailUsuario = usuarioIdentity?.Email;

        return View(config);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Salvar(Configuracao model, IFormFile? foto)
    {
        RemoverValidacaoUsuario();

        // ── Validação do upload de foto ─────────────────────────────────
        if (foto != null && foto.Length > 0)
        {
            if (foto.Length > TamanhoMaximoFotoBytes)
                ModelState.AddModelError("foto", "A foto não pode exceder 1 MB.");
            else if (!_mimesFotoPermitidos.Contains(foto.ContentType))
                ModelState.AddModelError("foto", "Formato inválido. Envie uma imagem JPEG, PNG ou WebP.");
            else if (!await ValidarMagicBytesFotoAsync(foto))
                ModelState.AddModelError("foto", "O arquivo enviado não é uma imagem válida.");
        }

        ValidarTamanhoTexto(nameof(model.NomeEstabelecimento), model.NomeEstabelecimento, 150);
        ValidarTamanhoTexto(nameof(model.Telefone),            model.Telefone,            20);
        ValidarTamanhoTexto(nameof(model.Endereco),            model.Endereco,            300);

        if (ModelState.IsValid)
        {
            var idEmpresa = await ObterIdEmpresaAsync();

            // Aqui PRECISAMOS de tracking — vamos atualizar a entidade
            var configExistente = await _contexto.Configuracao
                .FirstOrDefaultAsync(c => c.IdEmpresa == idEmpresa);

            if (foto != null && foto.Length > 0 && (ModelState["foto"] == null || ModelState["foto"]!.Errors.Count == 0))
            {
                using var memoryStream = new MemoryStream();
                await foto.CopyToAsync(memoryStream);
                model.FotoDados    = memoryStream.ToArray();
                model.FotoMimeType = foto.ContentType;
            }
            else if (configExistente != null)
            {
                model.FotoDados    = configExistente.FotoDados;
                model.FotoMimeType = configExistente.FotoMimeType;
            }

            if (configExistente != null)
            {
                configExistente.NomeEstabelecimento = model.NomeEstabelecimento;
                configExistente.Telefone            = model.Telefone;
                configExistente.Endereco            = model.Endereco;
                configExistente.FotoDados           = model.FotoDados;
                configExistente.FotoMimeType        = model.FotoMimeType;

                var empresa = await _contexto.Empresa.FindAsync(idEmpresa);
                if (empresa != null)
                    empresa.NomeEstabelecimento = model.NomeEstabelecimento;
            }
            else
            {
                model.IdEmpresa = idEmpresa;
                _contexto.Add(model);
            }

            await _contexto.SaveChangesAsync();

            // ─── INVALIDA cache do estabelecimento (usado no CarteiraController e Layout) ───
            _cache.Remove($"estabelecimento:{idEmpresa}");
            _cache.Remove($"clinica-pdf:{idEmpresa}");

            DefinirToast("Configurações salvas com sucesso!", "success");
            return RedirectToAction(nameof(Index));
        }

        var mensagemFoto = ModelState["foto"]?.Errors.FirstOrDefault()?.ErrorMessage;
        DefinirToast(mensagemFoto ?? "Verifique os campos obrigatórios.", "danger");
        return View("Index", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AtualizarEmail(string NovoEmail)
    {
        if (string.IsNullOrWhiteSpace(NovoEmail))
        {
            DefinirToast("O novo e-mail não pode ser vazio.", "warning");
            return RedirectToAction(nameof(Index));
        }

        if (NovoEmail.Length > 254 || !NovoEmail.Contains('@'))
        {
            DefinirToast("Informe um e-mail válido.", "warning");
            return RedirectToAction(nameof(Index));
        }

        var usuario = await _gerenciadorUsuarioLocal.GetUserAsync(User);
        if (usuario == null) return NotFound();

        if (usuario.Email == NovoEmail)
        {
            DefinirToast("O e-mail informado já é o seu e-mail atual.", "warning");
            return RedirectToAction(nameof(Index));
        }

        var emailJaExiste = await _gerenciadorUsuarioLocal.FindByEmailAsync(NovoEmail);
        if (emailJaExiste != null)
        {
            DefinirToast("Este e-mail já está em uso por outra conta.", "danger");
            return RedirectToAction(nameof(Index));
        }

        var token     = await _gerenciadorUsuarioLocal.GenerateChangeEmailTokenAsync(usuario, NovoEmail);
        var resultado = await _gerenciadorUsuarioLocal.ChangeEmailAsync(usuario, NovoEmail, token);

        if (resultado.Succeeded)
        {
            await _gerenciadorUsuarioLocal.SetUserNameAsync(usuario, NovoEmail);
            DefinirToast("E-mail atualizado com sucesso!", "success");
        }
        else
        {
            DefinirToast("Erro ao atualizar o e-mail.", "danger");
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AtualizarSenha(string SenhaAtual, string NovaSenha, string ConfirmarSenha)
    {
        if (string.IsNullOrWhiteSpace(SenhaAtual) || string.IsNullOrWhiteSpace(NovaSenha))
        {
            DefinirToast("Preencha todas as senhas.", "warning");
            return RedirectToAction(nameof(Index));
        }

        if (NovaSenha.Length > 128)
        {
            DefinirToast("A nova senha não pode exceder 128 caracteres.", "warning");
            return RedirectToAction(nameof(Index));
        }

        if (NovaSenha != ConfirmarSenha)
        {
            DefinirToast("A nova senha e a confirmação não conferem.", "warning");
            return RedirectToAction(nameof(Index));
        }

        var usuario = await _gerenciadorUsuarioLocal.GetUserAsync(User);
        if (usuario == null) return NotFound();

        var resultado = await _gerenciadorUsuarioLocal.ChangePasswordAsync(usuario, SenhaAtual, NovaSenha);

        if (resultado.Succeeded)
            DefinirToast("Senha alterada com sucesso!", "success");
        else
            DefinirToast("Não foi possível alterar a senha. Verifique a senha atual.", "danger");

        return RedirectToAction(nameof(Index));
    }

    private static async Task<bool> ValidarMagicBytesFotoAsync(IFormFile foto)
    {
        var buffer = new byte[8];
        await using var stream = foto.OpenReadStream();
        var lidos = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length));
        if (lidos < 4) return false;

        if (buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF)
            return foto.ContentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase);

        if (buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47)
            return foto.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase);

        if (buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46)
        {
            var bufferWebp = new byte[4];
            stream.Seek(8, SeekOrigin.Begin);
            var lidosWebp = await stream.ReadAsync(bufferWebp.AsMemory(0, 4));
            if (lidosWebp == 4 &&
                bufferWebp[0] == 0x57 && bufferWebp[1] == 0x45 &&
                bufferWebp[2] == 0x42 && bufferWebp[3] == 0x50)
                return foto.ContentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
    
    [AllowAnonymous]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> Foto(int idEmpresa, string? v = null)
    {
        // O parâmetro 'v' é só pra cache busting, ignorado aqui
        var foto = await _contextoBase.Configuracao
            .AsNoTracking()
            .Where(c => c.IdEmpresa == idEmpresa)
            .Select(c => new { c.FotoDados, c.FotoMimeType })
            .FirstOrDefaultAsync();

        if (foto?.FotoDados == null || foto.FotoDados.Length == 0)
            return NotFound();

        Response.Headers.CacheControl = "private, max-age=86400, immutable";
        return File(foto.FotoDados, foto.FotoMimeType ?? "image/jpeg");
    }
}
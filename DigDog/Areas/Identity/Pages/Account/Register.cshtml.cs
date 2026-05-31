// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace DigDog.Areas.Identity.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IUserStore<IdentityUser> _userStore;
        private readonly IUserEmailStore<IdentityUser> _emailStore;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly RoleManager<IdentityRole> _gerenciadorRole;
        private readonly DigDog.Data.Contexto _contexto;
        private readonly IConfiguration _configuracao;

        public RegisterModel(
            UserManager<IdentityUser> userManager,
            IUserStore<IdentityUser> userStore,
            SignInManager<IdentityUser> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender,
            RoleManager<IdentityRole> gerenciadorRole,
            DigDog.Data.Contexto contexto,
            IConfiguration configuracao)
        {
            _userManager      = userManager;
            _userStore        = userStore;
            _emailStore       = GetEmailStore();
            _signInManager    = signInManager;
            _logger           = logger;
            _emailSender      = emailSender;
            _gerenciadorRole  = gerenciadorRole;
            _contexto         = contexto;
            _configuracao     = configuracao;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "O e-mail é obrigatório.")]
            [EmailAddress(ErrorMessage = "E-mail inválido.")]
            [Display(Name = "E-mail")]
            public string Email { get; set; }

            [Required(ErrorMessage = "O CPF é obrigatório.")]
            [StringLength(14, ErrorMessage = "CPF inválido.")]
            [Display(Name = "CPF")]
            public string Cpf { get; set; }

            [Required(ErrorMessage = "A senha é obrigatória.")]
            [StringLength(100, ErrorMessage = "A senha deve ter no mínimo {2} e no máximo {1} caracteres.", MinimumLength = 6)]
            [DataType(DataType.Password)]
            [Display(Name = "Senha")]
            public string Password { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Confirmar senha")]
            [Compare("Password", ErrorMessage = "A senha e a confirmação não coincidem.")]
            public string ConfirmPassword { get; set; }

            [Required(ErrorMessage = "O código de convite é obrigatório.")]
            [Display(Name = "Código de Convite")]
            public string CodigoConvite { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            // Redireciona para login se acessar sem o parâmetro de convite
            if (!Request.Query.ContainsKey("convite"))
            {
                Response.Redirect("/Identity/Account/Login");
                return;
            }

            ReturnUrl = returnUrl;
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            // ── Valida código de convite antes de qualquer coisa ───────────
            var codigoValido = _configuracao["Registro:CodigoConvite"];
            if (Input.CodigoConvite != codigoValido)
            {
                ModelState.AddModelError(nameof(Input.CodigoConvite), "Código de convite inválido.");
                return Page();
            }

            if (ModelState.IsValid)
            {
                var usuario = CreateUser();

                await _userStore.SetUserNameAsync(usuario, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(usuario, Input.Email, CancellationToken.None);

                var resultado = await _userManager.CreateAsync(usuario, Input.Password);

                if (resultado.Succeeded)
                {
                    _logger.LogInformation("Nova conta criada com sucesso.");

                    await GarantirAdminAsync(usuario);

                    var cpfLimpo = new string(Input.Cpf.Where(char.IsDigit).ToArray());
                    await _userManager.AddClaimAsync(usuario,
                        new System.Security.Claims.Claim("Cpf", cpfLimpo));

                    var userId = await _userManager.GetUserIdAsync(usuario);
                    var code   = await _userManager.GenerateEmailConfirmationTokenAsync(usuario);
                    code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                    var callbackUrl = Url.Page(
                        "/Account/ConfirmEmail",
                        pageHandler: null,
                        values: new { area = "Identity", userId, code, returnUrl },
                        protocol: Request.Scheme);

                    await _emailSender.SendEmailAsync(
                        Input.Email,
                        "Confirme seu e-mail",
                        $"Confirme sua conta <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicando aqui</a>.");

                    if (_userManager.Options.SignIn.RequireConfirmedAccount)
                    {
                        return RedirectToPage("RegisterConfirmation",
                            new { email = Input.Email, returnUrl });
                    }
                    else
                    {
                        await _signInManager.SignInAsync(usuario, isPersistent: false);
                        return LocalRedirect(returnUrl);
                    }
                }

                foreach (var erro in resultado.Errors)
                    ModelState.AddModelError(string.Empty, erro.Description);
            }

            return Page();
        }

        private async Task GarantirAdminAsync(IdentityUser usuario)
        {
            if (!await _gerenciadorRole.RoleExistsAsync("Admin"))
                await _gerenciadorRole.CreateAsync(new IdentityRole("Admin"));

            await _userManager.AddToRoleAsync(usuario, "Admin");

            var empresa = new DigDog.Models.Empresa
            {
                NomeEstabelecimento = "DigDogVet",
                IdAdmin             = usuario.Id
            };

            _contexto.Empresa.Add(empresa);
            await _contexto.SaveChangesAsync();

            var empresaSalva = await _contexto.Empresa
                .Where(e => e.IdAdmin == usuario.Id)
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync();

            if (empresaSalva == null)
                throw new InvalidOperationException("Falha ao criar a empresa do usuário.");

            _contexto.EmpresaUsuario.Add(new DigDog.Models.EmpresaUsuario
            {
                IdEmpresa = empresaSalva.Id,
                IdUsuario = usuario.Id
            });

            var agora = DateTime.Now;
            _contexto.Assinatura.Add(new DigDog.Models.Assinatura
            {
                IdEmpresa     = empresaSalva.Id,
                DataInicio    = agora,
                DataExpiracao = agora.AddDays(30),
                Ativa         = true
            });

            await _contexto.SaveChangesAsync();
        }

        private IdentityUser CreateUser()
        {
            try
            {
                return Activator.CreateInstance<IdentityUser>();
            }
            catch
            {
                throw new InvalidOperationException(
                    $"Não foi possível criar uma instância de '{nameof(IdentityUser)}'. " +
                    $"Verifique se a classe não é abstrata e possui um construtor sem parâmetros.");
            }
        }

        private IUserEmailStore<IdentityUser> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
                throw new NotSupportedException("O sistema requer uma store de usuário com suporte a e-mail.");

            return (IUserEmailStore<IdentityUser>)_userStore;
        }
    }
}
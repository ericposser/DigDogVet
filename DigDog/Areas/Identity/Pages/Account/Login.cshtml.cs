#nullable disable

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DigDog.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _gerenciadorLogin;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(SignInManager<IdentityUser> gerenciadorLogin, ILogger<LoginModel> logger)
        {
            _gerenciadorLogin = gerenciadorLogin;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public IList<AuthenticationScheme> LoginsExternos { get; set; }

        public string UrlRetorno { get; set; }

        [TempData]
        public string MensagemErro { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "O e-mail é obrigatório.")]
            [EmailAddress(ErrorMessage = "E-mail em formato inválido.")]
            [Display(Name = "E-mail")]
            public string Email { get; set; }

            [Required(ErrorMessage = "A senha é obrigatória.")]
            [DataType(DataType.Password)]
            [Display(Name = "Senha")]
            public string Password { get; set; }

            [Display(Name = "Lembrar-me neste dispositivo")]
            public bool RememberMe { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            if (!string.IsNullOrEmpty(MensagemErro))
            {
                ModelState.AddModelError(string.Empty, MensagemErro);
            }

            returnUrl ??= Url.Content("~/");

            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            LoginsExternos = (await _gerenciadorLogin.GetExternalAuthenticationSchemesAsync()).ToList();

            UrlRetorno = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");

            LoginsExternos = (await _gerenciadorLogin.GetExternalAuthenticationSchemesAsync()).ToList();

            if (ModelState.IsValid)
            {
                var resultado = await _gerenciadorLogin.PasswordSignInAsync(
                    Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: false);

                if (resultado.Succeeded)
                {
                    _logger.LogInformation("Usuário autenticado com sucesso.");
                    return LocalRedirect(returnUrl);
                }

                if (resultado.RequiresTwoFactor)
                {
                    return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
                }

                if (resultado.IsLockedOut)
                {
                    _logger.LogWarning("Conta de usuário bloqueada.");
                    return RedirectToPage("./Lockout");
                }

                ModelState.AddModelError(string.Empty, "E-mail ou senha incorretos.");
                return Page();
            }

            return Page();
        }
    }
}
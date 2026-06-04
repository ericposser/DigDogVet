using DigDog.Data;
using DigDog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigDog.Controllers;

[Authorize]
public class AssinaturaController : UtilController
{
    private readonly Contexto _contexto;

    public AssinaturaController(
        Contexto contexto,
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao)
        : base(gerenciadorUsuario, provedorProtecao, contexto)
    {
        _contexto = contexto;
    }

    public async Task<IActionResult> Index()
    {
        var idEmpresa = await ObterIdEmpresaAsync();

        var assinatura = await _contexto.Assinatura
            .AsNoTracking()
            .Where(a => a.IdEmpresa == idEmpresa)
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync();

        return View(assinatura);
    }

    [AllowAnonymous]
    public async Task<IActionResult> Inativa()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            // ObterIdEmpresaAsync lança se não houver vínculo; aqui é tolerante
            try
            {
                ViewBag.IdEmpresa = await ObterIdEmpresaAsync();
            }
            catch (InvalidOperationException)
            {
                ViewBag.IdEmpresa = null;
            }
        }

        return View();
    }
}
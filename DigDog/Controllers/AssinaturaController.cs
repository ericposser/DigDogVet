using DigDog.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigDog.Controllers;

[Authorize]
public class AssinaturaController : Controller
{
    private readonly Contexto _contexto;
    private readonly UserManager<IdentityUser> _gerenciadorUsuario;

    public AssinaturaController(Contexto contexto, UserManager<IdentityUser> gerenciadorUsuario)
    {
        _contexto = contexto;
        _gerenciadorUsuario = gerenciadorUsuario;
    }

    public async Task<IActionResult> Index()
    {
        var idUsuario = _gerenciadorUsuario.GetUserId(User);

        var vinculo = await _contexto.EmpresaUsuario
            .FirstOrDefaultAsync(eu => eu.IdUsuario == idUsuario);

        if (vinculo == null) return NotFound();

        var assinatura = await _contexto.Assinatura
            .Where(a => a.IdEmpresa == vinculo.IdEmpresa)
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync();
        
        return View(assinatura);
    }

    [AllowAnonymous]
    public async Task<IActionResult> Inativa()
    {
        // Tenta obter o idEmpresa se o usuário estiver logado
        if (User.Identity?.IsAuthenticated == true)
        {
            var idUsuario = _gerenciadorUsuario.GetUserId(User);
            var vinculo   = await _contexto.EmpresaUsuario
                .FirstOrDefaultAsync(eu => eu.IdUsuario == idUsuario);

            ViewBag.IdEmpresa = vinculo?.IdEmpresa;
        }

        return View();
    }
}
using DigDog.Data;
using DigDog.Filters;
using DigDog.Models;
using DigDog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DigDog.Controllers;

[Authorize]
public class ConsultaController : UtilController
{
    private readonly Contexto _contexto;
    private readonly LogService _logService;

    public ConsultaController(
        Contexto contexto,
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao,
        LogService logService)
        : base(gerenciadorUsuario, provedorProtecao, contexto)
    {
        _contexto   = contexto;
        _logService = logService;
    }

    [RequerPermissao(Permissao.ConsultasVisualizar)]
    public async Task<IActionResult> Index()
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        var consultas = await _contexto.Consulta
            .Include(c => c.Pet)
            .ThenInclude(p => p!.Cliente)
            .Where(c => c.IdEmpresa == idEmpresa)
            .OrderByDescending(c => c.DataHora)
            .ToListAsync();
        return View(consultas);
    }

    [RequerPermissao(Permissao.ConsultasCriar)]
    public async Task<IActionResult> Create()
    {
        await CarregarPets(await ObterIdEmpresaAsync());
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.ConsultasCriar)]
    public async Task<IActionResult> Create([Bind("DataHora,Motivo,Diagnostico,Prescricao,Valor,IdPet")] Consulta consulta)
    {
        var idEmpresa      = await ObterIdEmpresaAsync();
        consulta.IdEmpresa = idEmpresa;
        RemoverValidacaoUsuario();

        // Validação de tamanho dos campos de texto livre
        ValidarTamanhoTexto(nameof(consulta.Motivo),      consulta.Motivo,      500);
        ValidarTamanhoTexto(nameof(consulta.Diagnostico), consulta.Diagnostico, 2000);
        ValidarTamanhoTexto(nameof(consulta.Prescricao),  consulta.Prescricao,  2000);

        var horarioOcupado = await _contexto.Consulta
            .AnyAsync(c => c.IdEmpresa == idEmpresa && c.DataHora == consulta.DataHora);
        if (horarioOcupado)
            ModelState.AddModelError(nameof(consulta.DataHora),
                "Já existe uma consulta marcada para este dia e horário.");

        if (ModelState.IsValid)
        {
            _contexto.Add(consulta);
            await _contexto.SaveChangesAsync();
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Criou", "Consultas",
                $"Registrou consulta para {consulta.DataHora:dd/MM/yyyy HH:mm}",
                idEmpresa);
            DefinirToast("Consulta registrada com sucesso!", "success");
            return RedirectToAction(nameof(Index));
        }
        await CarregarPets(idEmpresa, consulta.IdPet);
        return View(consulta);
    }

    [RequerPermissao(Permissao.ConsultasEditar)]
    public async Task<IActionResult> Edit(string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var consulta  = await _contexto.Consulta
            .Where(c => c.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(c => c.Id == idReal);
        if (consulta == null) return NotFound();
        await CarregarPets(idEmpresa, consulta.IdPet);
        ViewBag.IdCriptografado = CriptografarId(consulta.Id);
        return View(consulta);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.ConsultasEditar)]
    public async Task<IActionResult> Edit(string id, [Bind("DataHora,Motivo,Diagnostico,Prescricao,Valor,IdPet")] Consulta consulta)
    {
        var idRota = RouteData.Values["id"]?.ToString();
        var idReal = DescriptografarId(idRota ?? id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        RemoverValidacaoUsuario();

        // Validação de tamanho dos campos de texto livre
        ValidarTamanhoTexto(nameof(consulta.Motivo),      consulta.Motivo,      500);
        ValidarTamanhoTexto(nameof(consulta.Diagnostico), consulta.Diagnostico, 2000);
        ValidarTamanhoTexto(nameof(consulta.Prescricao),  consulta.Prescricao,  2000);

        var horarioOcupado = await _contexto.Consulta
            .AnyAsync(c => c.IdEmpresa == idEmpresa && c.DataHora == consulta.DataHora && c.Id != idReal);
        if (horarioOcupado)
            ModelState.AddModelError(nameof(consulta.DataHora),
                "Já existe uma consulta marcada para este dia e horário.");

        var consultaExistente = await _contexto.Consulta
            .Where(c => c.IdEmpresa == idEmpresa && c.Id == idReal)
            .FirstOrDefaultAsync();
        if (consultaExistente == null) return NotFound();

        if (ModelState.IsValid)
        {
            consultaExistente.DataHora    = consulta.DataHora;
            consultaExistente.Motivo      = consulta.Motivo;
            consultaExistente.Diagnostico = consulta.Diagnostico;
            consultaExistente.Prescricao  = consulta.Prescricao;
            consultaExistente.Valor       = consulta.Valor;
            consultaExistente.IdPet       = consulta.IdPet;
            try { await _contexto.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                if (!ConsultaExiste(idReal.Value, idEmpresa)) return NotFound();
                throw;
            }
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Editou", "Consultas",
                $"Atualizou consulta de {consultaExistente.DataHora:dd/MM/yyyy HH:mm}",
                idEmpresa);
            DefinirToast("Consulta atualizada com sucesso!", "warning");
            return RedirectToAction(nameof(Index));
        }
        await CarregarPets(idEmpresa, consulta.IdPet);
        ViewBag.IdCriptografado = CriptografarId(idReal.Value);
        return View(consulta);
    }

    [RequerPermissao(Permissao.ConsultasExcluir)]
    public async Task<IActionResult> Delete(string id)
    {
        var idReal    = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var consulta  = await _contexto.Consulta
            .Include(c => c.Pet)
            .Where(c => c.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(c => c.Id == idReal);
        if (consulta == null) return NotFound();
        ViewBag.IdCriptografado = CriptografarId(consulta.Id);
        return View(consulta);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.ConsultasExcluir)]
    public async Task<IActionResult> DeleteConfirmed([FromRoute] string id)
    {
        var idReal    = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var consulta  = await _contexto.Consulta
            .Include(c => c.Pet)
            .Where(c => c.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(c => c.Id == idReal);
        if (consulta != null)
        {
            var descricao = $"Removeu consulta de {consulta.Pet?.Nome} em {consulta.DataHora:dd/MM/yyyy HH:mm}";
            _contexto.Consulta.Remove(consulta);
            await _contexto.SaveChangesAsync();
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Excluiu", "Consultas",
                descricao,
                idEmpresa);
        }
        DefinirToast("Consulta excluída com sucesso!", "danger");
        return RedirectToAction(nameof(Index));
    }

    private bool ConsultaExiste(int id, int idEmpresa) =>
        _contexto.Consulta.Any(c => c.Id == id && c.IdEmpresa == idEmpresa);

    private async Task CarregarPets(int idEmpresa, int? idSelecionado = null)
    {
        var pets = await _contexto.Pet
            .Where(p => p.IdEmpresa == idEmpresa)
            .OrderBy(p => p.Nome)
            .ToListAsync();
        ViewData["IdPet"] = new SelectList(pets, "Id", "Nome", idSelecionado);
    }
}

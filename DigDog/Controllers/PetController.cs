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
using Microsoft.Extensions.Caching.Memory;

namespace DigDog.Controllers;

[Authorize]
public class PetController : UtilController
{
    private readonly Contexto _contexto;
    private readonly LogService _logService;

    public PetController(
        Contexto contexto,
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao,
        LogService logService,
        IMemoryCache cache)
        : base(gerenciadorUsuario, provedorProtecao, contexto, cache)
    {
        _contexto   = contexto;
        _logService = logService;
    }

    [RequerPermissao(Permissao.PetsVisualizar)]
    public async Task<IActionResult> Index()
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        var pets = await _contexto.Pet
            .AsNoTracking()
            .Include(p => p.Cliente)
            .Where(p => p.IdEmpresa == idEmpresa)
            .OrderBy(p => p.Nome)
            .ToListAsync();
        return View(pets);
    }

    [RequerPermissao(Permissao.PetsCriar)]
    public async Task<IActionResult> Create()
    {
        await CarregarTutores(await ObterIdEmpresaAsync());
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.PetsCriar)]
    public async Task<IActionResult> Create([Bind("Nome,Especie,Raca,DataNascimento,Sexo,IdCliente")] Pet pet)
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        pet.IdEmpresa = idEmpresa;
        RemoverValidacaoUsuario();
        if (pet.DataNascimento.HasValue && pet.DataNascimento.Value > DateTime.Today)
            ModelState.AddModelError(nameof(pet.DataNascimento), "A data de nascimento não pode ser futura.");
        if (ModelState.IsValid)
        {
            _contexto.Add(pet);
            await _contexto.SaveChangesAsync();
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Criou", "Pets",
                $"Cadastrou o pet {pet.Nome}",
                idEmpresa);
            DefinirToast("Pet cadastrado com sucesso!", "success");
            return RedirectToAction(nameof(Index));
        }
        await CarregarTutores(idEmpresa, pet.IdCliente);
        return View(pet);
    }

    [RequerPermissao(Permissao.PetsEditar)]
    public async Task<IActionResult> Edit(string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var pet = await _contexto.Pet
            .AsNoTracking()
            .Where(p => p.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(p => p.Id == idReal);
        if (pet == null) return NotFound();
        await CarregarTutores(idEmpresa, pet.IdCliente);
        ViewBag.IdCriptografado = CriptografarId(pet.Id);
        return View(pet);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.PetsEditar)]
    public async Task<IActionResult> Edit(string id, [Bind("Nome,Especie,Raca,DataNascimento,Sexo,IdCliente")] Pet pet)
    {
        var idRota = RouteData.Values["id"]?.ToString();
        var idReal = DescriptografarId(idRota ?? id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        RemoverValidacaoUsuario();
        if (pet.DataNascimento.HasValue && pet.DataNascimento.Value > DateTime.Today)
            ModelState.AddModelError(nameof(pet.DataNascimento), "A data de nascimento não pode ser futura.");
        var petExistente = await _contexto.Pet
            .Where(p => p.IdEmpresa == idEmpresa && p.Id == idReal)
            .FirstOrDefaultAsync();
        if (petExistente == null) return NotFound();
        if (ModelState.IsValid)
        {
            petExistente.Nome           = pet.Nome;
            petExistente.Especie        = pet.Especie;
            petExistente.Raca           = pet.Raca;
            petExistente.DataNascimento = pet.DataNascimento;
            petExistente.Sexo           = pet.Sexo;
            petExistente.IdCliente      = pet.IdCliente;
            try { await _contexto.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                if (!await PetExisteAsync(idReal.Value, idEmpresa)) return NotFound();
                throw;
            }
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Editou", "Pets",
                $"Atualizou o pet {petExistente.Nome}",
                idEmpresa);
            DefinirToast("Pet atualizado com sucesso!", "warning");
            return RedirectToAction(nameof(Index));
        }
        await CarregarTutores(idEmpresa, pet.IdCliente);
        ViewBag.IdCriptografado = CriptografarId(idReal.Value);
        return View(pet);
    }

    [RequerPermissao(Permissao.PetsExcluir)]
    public async Task<IActionResult> Delete(string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var pet = await _contexto.Pet
            .AsNoTracking()
            .Include(p => p.Cliente)
            .Where(p => p.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(p => p.Id == idReal);
        if (pet == null) return NotFound();
        ViewBag.IdCriptografado = CriptografarId(pet.Id);
        return View(pet);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.PetsExcluir)]
    public async Task<IActionResult> DeleteConfirmed([FromRoute] string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var pet = await _contexto.Pet
            .Where(p => p.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(p => p.Id == idReal);
        if (pet != null)
        {
            var nome = pet.Nome;
            _contexto.Pet.Remove(pet);
            await _contexto.SaveChangesAsync();
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Excluiu", "Pets",
                $"Removeu o pet {nome}",
                idEmpresa);
        }
        DefinirToast("Pet excluído com sucesso!", "danger");
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> PetExisteAsync(int id, int idEmpresa) =>
        await _contexto.Pet.AnyAsync(p => p.Id == id && p.IdEmpresa == idEmpresa);

    private async Task CarregarTutores(int idEmpresa, int? idSelecionado = null)
    {
        // Projeção: traz só Id e Nome em vez do Cliente inteiro (com CPF, endereço, etc)
        var tutores = await _contexto.Cliente
            .AsNoTracking()
            .Where(c => c.IdEmpresa == idEmpresa)
            .OrderBy(c => c.Nome)
            .Select(c => new { c.Id, c.Nome })
            .ToListAsync();
        ViewData["IdCliente"] = new SelectList(tutores, "Id", "Nome", idSelecionado);
    }
}
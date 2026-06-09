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
public class VacinacaoController : UtilController
{
    private const int MaxVacinasPorAplicacao = 20;

    private readonly Contexto _contexto;
    private readonly LogService _logService;

    public VacinacaoController(
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

    [RequerPermissao(Permissao.VacinacaoVisualizar)]
    public async Task<IActionResult> Index()
    {
        var idEmpresa = await ObterIdEmpresaAsync();

        var vacinacoes = await _contexto.Vacinacao
            .AsNoTracking()
            .Include(v => v.Pet)
            .Include(v => v.Vacina)
            .Where(v => v.IdEmpresa == idEmpresa)
            .ToListAsync();
        
        vacinacoes = vacinacoes
            .OrderBy(v =>
            {
                var proxima = v.DataProximaDose;
                if (!proxima.HasValue) return (2, DateTime.MaxValue);
                if (proxima.Value < DateTime.Today) return (0, proxima.Value);
                if ((proxima.Value - DateTime.Today).TotalDays <= 7) return (1, proxima.Value);
                return (2, proxima.Value);
            })
            .ThenBy(v => v.DataProximaDose ?? DateTime.MaxValue)
            .ThenBy(v => v.IdPet)
            .ToList();

        var idsDosPets = vacinacoes
            .Where(v => v.IdPet > 0)
            .Select(v => v.IdPet)
            .Distinct().ToList();

        var tokensAtivos = await _contexto.CarteiraToken
            .AsNoTracking()
            .Where(t => t.IdEmpresa == idEmpresa && t.Ativo && idsDosPets.Contains(t.IdPet))
            .Select(t => new { t.IdPet, t.Token })
            .ToListAsync();

        ViewBag.TokensAtivos = tokensAtivos.ToDictionary(
            t => t.IdPet,
            t => Url.Action("Publico", "Carteira", new { token = t.Token }, Request.Scheme)!);

        return View(vacinacoes);
    }

    [RequerPermissao(Permissao.VacinacaoCriar)]
    public async Task<IActionResult> Create(string? pet)
    {
        var idEmpresa = await ObterIdEmpresaAsync();

        int? idPetPreSelecionado = null;
        if (pet != null)
        {
            idPetPreSelecionado = DescriptografarId(pet);
            if (idPetPreSelecionado == null) return NotFound();
        }

        await CarregarPets(idEmpresa, idPetPreSelecionado);
        await CarregarVacinas(idEmpresa);
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.VacinacaoCriar)]
    public async Task<IActionResult> Create(
        [Bind("IdPet,DataAplicacao,DataProximaDose,Observacoes")] Vacinacao vacinacao,
        List<VacinaItem> Vacinas)
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        RemoverValidacaoUsuario();

        ValidarTamanhoTexto(nameof(vacinacao.Observacoes), vacinacao.Observacoes, 1000);
        ValidarDatas(vacinacao);

        if (Vacinas == null || !Vacinas.Any())
            ModelState.AddModelError(string.Empty, "Adicione pelo menos uma vacina.");
        else if (Vacinas.Count > MaxVacinasPorAplicacao)
            ModelState.AddModelError(string.Empty,
                $"O máximo de vacinas por aplicação é {MaxVacinasPorAplicacao}.");
        else
        {
            for (int i = 0; i < Vacinas.Count; i++)
                ValidarTamanhoTexto($"Vacinas[{i}].Dose", Vacinas[i].Dose, 100);
        }

        if (ModelState.IsValid)
        {
            var novasVacinacoes = Vacinas!.Select(item => new Vacinacao
            {
                IdPet           = vacinacao.IdPet,
                IdVacina        = item.IdVacina,
                Dose            = item.Dose,
                DataAplicacao   = vacinacao.DataAplicacao,
                DataProximaDose = vacinacao.DataProximaDose,
                Observacoes     = vacinacao.Observacoes,
                IdEmpresa       = idEmpresa
            }).ToList();

            _contexto.Vacinacao.AddRange(novasVacinacoes);
            await _contexto.SaveChangesAsync();

            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Criou", "Carteira de Vacinação",
                $"Registrou {novasVacinacoes.Count} vacinação(ões) em {vacinacao.DataAplicacao:dd/MM/yyyy}",
                idEmpresa);
            DefinirToast($"{novasVacinacoes.Count} vacinação(ões) registrada(s) com sucesso!", "success");
            return RedirectToAction(nameof(Index));
        }
        await CarregarPets(idEmpresa, vacinacao.IdPet);
        await CarregarVacinas(idEmpresa);
        return View(vacinacao);
    }

    [RequerPermissao(Permissao.VacinacaoEditar)]
    public async Task<IActionResult> Edit(string id)
    {
        var idReal    = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var vacinacao = await _contexto.Vacinacao
            .AsNoTracking()
            .Where(v => v.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(v => v.Id == idReal);
        if (vacinacao == null) return NotFound();
        await CarregarPets(idEmpresa, vacinacao.IdPet);
        await CarregarVacinas(idEmpresa, vacinacao.IdVacina);
        ViewBag.IdCriptografado = CriptografarId(vacinacao.Id);
        return View(vacinacao);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.VacinacaoEditar)]
    public async Task<IActionResult> Edit(string id,
        [Bind("Dose,DataAplicacao,DataProximaDose,Observacoes,IdVacina,IdPet")] Vacinacao vacinacao)
    {
        var idRota = RouteData.Values["id"]?.ToString();
        var idReal = DescriptografarId(idRota ?? id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        RemoverValidacaoUsuario();

        ValidarTamanhoTexto(nameof(vacinacao.Observacoes), vacinacao.Observacoes, 1000);
        ValidarTamanhoTexto(nameof(vacinacao.Dose),        vacinacao.Dose,        100);
        ValidarDatas(vacinacao);

        var vacinacaoExistente = await _contexto.Vacinacao
            .Where(v => v.IdEmpresa == idEmpresa && v.Id == idReal)
            .FirstOrDefaultAsync();
        if (vacinacaoExistente == null) return NotFound();

        if (ModelState.IsValid)
        {
            vacinacaoExistente.Dose            = vacinacao.Dose;
            vacinacaoExistente.DataAplicacao   = vacinacao.DataAplicacao;
            vacinacaoExistente.DataProximaDose = vacinacao.DataProximaDose;
            vacinacaoExistente.Observacoes     = vacinacao.Observacoes;
            vacinacaoExistente.IdVacina        = vacinacao.IdVacina;
            vacinacaoExistente.IdPet           = vacinacao.IdPet;
            try { await _contexto.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                if (!await VacinacaoExisteAsync(idReal.Value, idEmpresa)) return NotFound();
                throw;
            }
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Editou", "Carteira de Vacinação",
                $"Atualizou vacinação de {vacinacaoExistente.DataAplicacao:dd/MM/yyyy}",
                idEmpresa);
            DefinirToast("Vacinação atualizada com sucesso!", "warning");
            return RedirectToAction(nameof(Index));
        }
        await CarregarPets(idEmpresa, vacinacao.IdPet);
        await CarregarVacinas(idEmpresa, vacinacao.IdVacina);
        ViewBag.IdCriptografado = CriptografarId(idReal.Value);
        return View(vacinacao);
    }

    [RequerPermissao(Permissao.VacinacaoExcluir)]
    public async Task<IActionResult> Delete(string id)
    {
        var idReal    = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var vacinacao = await _contexto.Vacinacao
            .AsNoTracking()
            .Include(v => v.Pet)
            .Include(v => v.Vacina)
            .Where(v => v.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(v => v.Id == idReal);
        if (vacinacao == null) return NotFound();
        ViewBag.IdCriptografado = CriptografarId(vacinacao.Id);
        return View(vacinacao);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.VacinacaoExcluir)]
    public async Task<IActionResult> DeleteConfirmed([FromRoute] string id)
    {
        var idReal    = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        var vacinacao = await _contexto.Vacinacao
            .Include(v => v.Pet)
            .Include(v => v.Vacina)
            .Where(v => v.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(v => v.Id == idReal);
        if (vacinacao != null)
        {
            var descricao = $"Removeu vacinação de {vacinacao.Pet?.Nome} — {vacinacao.Vacina?.Nome}";
            _contexto.Vacinacao.Remove(vacinacao);
            await _contexto.SaveChangesAsync();
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Excluiu", "Carteira de Vacinação",
                descricao,
                idEmpresa);
        }
        DefinirToast("Vacinação excluída com sucesso!", "danger");
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> VacinacaoExisteAsync(int id, int idEmpresa) =>
        await _contexto.Vacinacao.AnyAsync(v => v.Id == id && v.IdEmpresa == idEmpresa);

    private async Task CarregarPets(int idEmpresa, int? idSelecionado = null)
    {
        var pets = await _contexto.Pet
            .AsNoTracking()
            .Where(p => p.IdEmpresa == idEmpresa)
            .OrderBy(p => p.Nome)
            .Select(p => new { p.Id, p.Nome })
            .ToListAsync();
        ViewData["IdPet"] = new SelectList(pets, "Id", "Nome", idSelecionado);
    }

    private async Task CarregarVacinas(int idEmpresa, int? idSelecionado = null)
    {
        var vacinas = await _contexto.Vacina
            .AsNoTracking()
            .Where(v => v.IdEmpresa == idEmpresa)
            .OrderBy(v => v.Nome)
            .Select(v => new { v.Id, v.Nome })
            .ToListAsync();
        ViewData["IdVacina"] = new SelectList(vacinas, "Id", "Nome", idSelecionado);
    }

    private void ValidarDatas(Vacinacao vacinacao)
    {
        if (vacinacao.DataAplicacao > DateTime.Today)
            ModelState.AddModelError(nameof(vacinacao.DataAplicacao),
                "A data de aplicação não pode ser futura.");
        if (vacinacao.DataProximaDose.HasValue && vacinacao.DataProximaDose.Value <= vacinacao.DataAplicacao)
            ModelState.AddModelError(nameof(vacinacao.DataProximaDose),
                "A data da próxima dose deve ser posterior à data de aplicação.");
    }

    public class VacinaItem
    {
        public int IdVacina { get; set; }
        public string? Dose { get; set; }
    }
}
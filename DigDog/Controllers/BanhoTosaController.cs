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
public class BanhoTosaController : UtilController
{
    private readonly Contexto _contexto;
    private readonly LogService _logService;

    public BanhoTosaController(
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

    [RequerPermissao(Permissao.BanhoTosaVisualizar)]
    public async Task<IActionResult> Index()
    {
        var idEmpresa = await ObterIdEmpresaAsync();

        var banhosTosas = await _contexto.BanhoTosa
            .AsNoTracking()
            .Include(b => b.Pet)
                .ThenInclude(p => p!.Cliente)
            .Where(b => b.IdEmpresa == idEmpresa)
            .OrderByDescending(b => b.DataHora)
            .ToListAsync();

        return View(banhosTosas);
    }

    [RequerPermissao(Permissao.BanhoTosaCriar)]
    public async Task<IActionResult> Create()
    {
        await CarregarPets(await ObterIdEmpresaAsync());
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.BanhoTosaCriar)]
    public async Task<IActionResult> Create([Bind("DataHora,TipoServico,Observacoes,Valor,IdPet")] BanhoTosa banhoTosa)
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        banhoTosa.IdEmpresa = idEmpresa;
        RemoverValidacaoUsuario();

        var horarioOcupado = await _contexto.BanhoTosa
            .AnyAsync(b => b.IdEmpresa == idEmpresa && b.DataHora == banhoTosa.DataHora);
        if (horarioOcupado)
            ModelState.AddModelError(nameof(banhoTosa.DataHora), "Já existe um agendamento para este dia e horário.");

        if (ModelState.IsValid)
        {
            _contexto.Add(banhoTosa);
            await _contexto.SaveChangesAsync();
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Criou", "Banho & Tosa",
                $"Agendou {banhoTosa.TipoServico} para {banhoTosa.DataHora:dd/MM/yyyy HH:mm}",
                idEmpresa);
            DefinirToast("Agendamento registrado com sucesso!", "success");
            return RedirectToAction(nameof(Index));
        }
        await CarregarPets(idEmpresa, banhoTosa.IdPet);
        return View(banhoTosa);
    }

    [RequerPermissao(Permissao.BanhoTosaEditar)]
    public async Task<IActionResult> Edit(string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();

        var banhoTosa = await _contexto.BanhoTosa
            .AsNoTracking()
            .Where(b => b.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(b => b.Id == idReal);
        if (banhoTosa == null) return NotFound();

        await CarregarPets(idEmpresa, banhoTosa.IdPet);
        ViewBag.IdCriptografado = CriptografarId(banhoTosa.Id);
        return View(banhoTosa);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.BanhoTosaEditar)]
    public async Task<IActionResult> Edit(string id, [Bind("DataHora,TipoServico,Observacoes,Valor,IdPet")] BanhoTosa banhoTosa)
    {
        var idRota = RouteData.Values["id"]?.ToString();
        var idReal = DescriptografarId(idRota ?? id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();
        RemoverValidacaoUsuario();

        // Busca primeiro o registro existente (necessário pra atualizar)
        var banhoTosaExistente = await _contexto.BanhoTosa
            .Where(b => b.IdEmpresa == idEmpresa && b.Id == idReal)
            .FirstOrDefaultAsync();
        if (banhoTosaExistente == null) return NotFound();

        // Só checa conflito de horário se a data realmente mudou (economiza 1 query)
        if (banhoTosaExistente.DataHora != banhoTosa.DataHora)
        {
            var horarioOcupado = await _contexto.BanhoTosa
                .AnyAsync(b => b.IdEmpresa == idEmpresa && b.DataHora == banhoTosa.DataHora && b.Id != idReal);
            if (horarioOcupado)
                ModelState.AddModelError(nameof(banhoTosa.DataHora), "Já existe um agendamento para este dia e horário.");
        }

        if (ModelState.IsValid)
        {
            banhoTosaExistente.DataHora    = banhoTosa.DataHora;
            banhoTosaExistente.TipoServico = banhoTosa.TipoServico;
            banhoTosaExistente.Observacoes = banhoTosa.Observacoes;
            banhoTosaExistente.Valor       = banhoTosa.Valor;
            banhoTosaExistente.IdPet       = banhoTosa.IdPet;
            try { await _contexto.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                if (!await BanhoTosaExisteAsync(idReal.Value, idEmpresa)) return NotFound();
                throw;
            }
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Editou", "Banho & Tosa",
                $"Atualizou agendamento de {banhoTosaExistente.DataHora:dd/MM/yyyy HH:mm}",
                idEmpresa);
            DefinirToast("Agendamento atualizado com sucesso!", "warning");
            return RedirectToAction(nameof(Index));
        }
        await CarregarPets(idEmpresa, banhoTosa.IdPet);
        ViewBag.IdCriptografado = CriptografarId(idReal.Value);
        return View(banhoTosa);
    }

    [RequerPermissao(Permissao.BanhoTosaExcluir)]
    public async Task<IActionResult> Delete(string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();

        var banhoTosa = await _contexto.BanhoTosa
            .AsNoTracking()
            .Include(b => b.Pet)
            .Where(b => b.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(b => b.Id == idReal);
        if (banhoTosa == null) return NotFound();

        ViewBag.IdCriptografado = CriptografarId(banhoTosa.Id);
        return View(banhoTosa);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.BanhoTosaExcluir)]
    public async Task<IActionResult> DeleteConfirmed([FromRoute] string id)
    {
        var idReal = DescriptografarId(id);
        if (idReal == null) return NotFound();
        var idEmpresa = await ObterIdEmpresaAsync();

        var banhoTosa = await _contexto.BanhoTosa
            .Include(b => b.Pet)
            .Where(b => b.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(b => b.Id == idReal);
        if (banhoTosa != null)
        {
            var descricao = $"Removeu agendamento de {banhoTosa.Pet?.Nome} em {banhoTosa.DataHora:dd/MM/yyyy HH:mm}";
            _contexto.BanhoTosa.Remove(banhoTosa);
            await _contexto.SaveChangesAsync();
            await _logService.RegistrarAsync(
                await ObterNomeFuncionarioAsync(),
                "Excluiu", "Banho & Tosa",
                descricao,
                idEmpresa);
        }
        DefinirToast("Agendamento excluído com sucesso!", "danger");
        return RedirectToAction(nameof(Index));
    }

    [RequerPermissao(Permissao.BanhoTosaCriar)]
    public async Task<IActionResult> CreateLote()
    {
        await CarregarPets(await ObterIdEmpresaAsync());
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.BanhoTosaCriar)]
    public async Task<IActionResult> CreateLote(
        [Bind("IdPet,TipoServico,Observacoes,Valor")] BanhoTosa banhoTosaBase,
        int[] diasSemana, string horario, string dataInicio, string dataFim)
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        banhoTosaBase.IdEmpresa = idEmpresa;
        RemoverValidacaoUsuario();

        if (diasSemana == null || diasSemana.Length == 0)
            ModelState.AddModelError("diasSemana", "Selecione pelo menos um dia da semana.");
        if (string.IsNullOrWhiteSpace(horario))
            ModelState.AddModelError("horario", "Informe o horário do agendamento.");
        if (!DateOnly.TryParse(dataInicio, out var inicio))
            ModelState.AddModelError("dataInicio", "Data de início inválida.");
        if (!DateOnly.TryParse(dataFim, out var fim))
            ModelState.AddModelError("dataFim", "Data de fim inválida.");
        if (ModelState.ErrorCount == 0 && fim < inicio)
            ModelState.AddModelError("dataFim", "A data de fim deve ser posterior à data de início.");
        if (ModelState.ErrorCount == 0 && (fim.ToDateTime(TimeOnly.MinValue) - inicio.ToDateTime(TimeOnly.MinValue)).TotalDays > 365)
            ModelState.AddModelError("dataFim", "O período não pode ser superior a 365 dias.");
        if (!TimeOnly.TryParse(horario, out var hora))
            ModelState.AddModelError("horario", "Formato de horário inválido.");

        if (!ModelState.IsValid)
        {
            await CarregarPets(idEmpresa, banhoTosaBase.IdPet);
            return View(banhoTosaBase);
        }

        var diasDaSemana = diasSemana!.Select(d => (DayOfWeek)d).ToHashSet();

        // ─── OTIMIZAÇÃO: busca TODOS os horários ocupados do período em UMA query ───
        var inicioPeriodo = inicio.ToDateTime(TimeOnly.MinValue);
        var fimPeriodo    = fim.ToDateTime(TimeOnly.MaxValue);
        var horariosOcupados = (await _contexto.BanhoTosa
                .AsNoTracking()
                .Where(b => b.IdEmpresa == idEmpresa
                            && b.DataHora >= inicioPeriodo
                            && b.DataHora <= fimPeriodo)
                .Select(b => b.DataHora)
                .ToListAsync())
            .ToHashSet();

        var agendamentosGerados = new List<BanhoTosa>();
        var dataAtual = inicio;

        while (dataAtual <= fim)
        {
            if (diasDaSemana.Contains(dataAtual.DayOfWeek))
            {
                var dataHora = dataAtual.ToDateTime(hora);
                if (!horariosOcupados.Contains(dataHora))
                {
                    agendamentosGerados.Add(new BanhoTosa
                    {
                        IdPet       = banhoTosaBase.IdPet,
                        TipoServico = banhoTosaBase.TipoServico,
                        Observacoes = banhoTosaBase.Observacoes,
                        Valor       = banhoTosaBase.Valor,
                        IdEmpresa   = idEmpresa,
                        DataHora    = dataHora
                    });
                    horariosOcupados.Add(dataHora); // evita duplicata no próprio lote
                }
            }
            dataAtual = dataAtual.AddDays(1);
        }

        if (agendamentosGerados.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Nenhum agendamento pôde ser gerado.");
            await CarregarPets(idEmpresa, banhoTosaBase.IdPet);
            return View(banhoTosaBase);
        }

        _contexto.BanhoTosa.AddRange(agendamentosGerados);
        await _contexto.SaveChangesAsync();
        await _logService.RegistrarAsync(
            await ObterNomeFuncionarioAsync(),
            "Criou", "Banho & Tosa",
            $"Criou {agendamentosGerados.Count} agendamento(s) em lote de {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}",
            idEmpresa);
        DefinirToast($"{agendamentosGerados.Count} agendamento(s) criado(s) com sucesso!", "success");
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> BanhoTosaExisteAsync(int id, int idEmpresa) =>
        await _contexto.BanhoTosa.AnyAsync(b => b.Id == id && b.IdEmpresa == idEmpresa);

    private async Task CarregarPets(int idEmpresa, int? idSelecionado = null)
    {
        // Projeção: traz só Id e Nome em vez do Pet inteiro (com Fotos, Cliente, etc)
        var pets = await _contexto.Pet
            .AsNoTracking()
            .Where(p => p.IdEmpresa == idEmpresa)
            .OrderBy(p => p.Nome)
            .Select(p => new { p.Id, p.Nome })
            .ToListAsync();
        ViewData["IdPet"] = new SelectList(pets, "Id", "Nome", idSelecionado);
    }
}
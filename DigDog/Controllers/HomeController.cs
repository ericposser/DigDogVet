using DigDog.Data;
using DigDog.Models;
using DigDog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DigDog.Controllers;

[Authorize]
public class HomeController : UtilController
{
    private readonly Contexto _contexto;
    private readonly PermissaoService _permissaoService;
    private readonly IDbContextFactory<Contexto> _fabricaContexto;
    private readonly IMemoryCache _cache;

    public HomeController(
        Contexto contexto,
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao,
        PermissaoService permissaoService,
        IDbContextFactory<Contexto> fabricaContexto,
        IMemoryCache cache)
        : base(gerenciadorUsuario, provedorProtecao, contexto, cache)
    {
        _contexto         = contexto;
        _permissaoService = permissaoService;
        _fabricaContexto  = fabricaContexto;
        _cache            = cache;
    }

    public async Task<IActionResult> Index()
    {
        var idEmpresa = await ObterIdEmpresaAsync();
        var idUsuario = ObterIdUsuario();

        var podeFaturamento = User.IsInRole("Admin") ||
            await _permissaoService.UsuarioTemPermissaoAsync(
                idUsuario, Permissao.PainelFaturamentoVisualizar);

        // ─── CACHE DO PAINEL (60s, por empresa e por perfil de faturamento) ───
        var chaveCache = $"painel:{idEmpresa}:fat-{podeFaturamento}";
        if (_cache.TryGetValue<PainelViewModel>(chaveCache, out var modeloCache) && modeloCache != null)
        {
            ViewBag.PodeFaturamento = podeFaturamento;
            return View(modeloCache);
        }

        var hoje             = DateTime.Today;
        var seteDias         = hoje.AddDays(7);
        var inicioMes        = new DateTime(hoje.Year, hoje.Month, 1);
        var inicioMesPassado = inicioMes.AddMonths(-1);
        var fimMesPassado    = inicioMes.AddDays(-1);
        var agora            = DateTime.Now;

        // ─── Contadores e agenda paralelizados ───────────────────────────
        var tarefaTotalClientes      = ExecutarComNovoContextoAsync(db =>
            db.Cliente.AsNoTracking().CountAsync(c => c.IdEmpresa == idEmpresa));
        var tarefaTotalPets          = ExecutarComNovoContextoAsync(db =>
            db.Pet.AsNoTracking().CountAsync(p => p.IdEmpresa == idEmpresa));
        var tarefaTotalProdutos      = ExecutarComNovoContextoAsync(db =>
            db.Produto.AsNoTracking().CountAsync(p => p.IdEmpresa == idEmpresa));
        var tarefaProdutosSemEstoque = ExecutarComNovoContextoAsync(db =>
            db.Produto.AsNoTracking().CountAsync(p => p.IdEmpresa == idEmpresa && p.Estoque == 0));
        var tarefaConsultasHoje      = ExecutarComNovoContextoAsync(db =>
            db.Consulta.AsNoTracking().CountAsync(c => c.IdEmpresa == idEmpresa && c.DataHora.Date == hoje));
        var tarefaBanhosHoje         = ExecutarComNovoContextoAsync(db =>
            db.BanhoTosa.AsNoTracking().CountAsync(b => b.IdEmpresa == idEmpresa && b.DataHora.Date == hoje));

        var tarefaProxConsultas = ExecutarComNovoContextoAsync(db =>
            db.Consulta.AsNoTracking()
                .Include(c => c.Pet)
                .Where(c => c.IdEmpresa == idEmpresa && c.DataHora >= agora)
                .OrderBy(c => c.DataHora).Take(5).ToListAsync());

        var tarefaProxBanhos = ExecutarComNovoContextoAsync(db =>
            db.BanhoTosa.AsNoTracking()
                .Include(b => b.Pet)
                .Where(b => b.IdEmpresa == idEmpresa && b.DataHora >= agora)
                .OrderBy(b => b.DataHora).Take(5).ToListAsync());

        var tarefaVacinasVenc = ExecutarComNovoContextoAsync(db =>
            db.Vacina.AsNoTracking()
                .Where(v => v.IdEmpresa == idEmpresa && v.DataValidade >= hoje && v.DataValidade <= seteDias)
                .OrderBy(v => v.DataValidade).ToListAsync());

        var tarefaProxDoses = ExecutarComNovoContextoAsync(db =>
            db.Vacinacao.AsNoTracking()
                .Include(v => v.Pet).Include(v => v.Vacina)
                .Where(v => v.IdEmpresa == idEmpresa
                         && v.DataProximaDose.HasValue
                         && v.DataProximaDose.Value >= hoje
                         && v.DataProximaDose.Value <= seteDias)
                .OrderBy(v => v.DataProximaDose).Take(5).ToListAsync());

        var tarefaDosesAtrasadas = ExecutarComNovoContextoAsync(db =>
            db.Vacinacao.AsNoTracking()
                .Include(v => v.Pet).Include(v => v.Vacina)
                .Where(v => v.IdEmpresa == idEmpresa
                         && v.DataProximaDose.HasValue
                         && v.DataProximaDose.Value < hoje)
                .OrderBy(v => v.DataProximaDose).Take(5).ToListAsync());

        await Task.WhenAll(
            tarefaTotalClientes, tarefaTotalPets, tarefaTotalProdutos, tarefaProdutosSemEstoque,
            tarefaConsultasHoje, tarefaBanhosHoje,
            tarefaProxConsultas, tarefaProxBanhos,
            tarefaVacinasVenc, tarefaProxDoses, tarefaDosesAtrasadas);

        // ── Faturamento (apenas para quem tem permissão) ─────────────────
        decimal receitaConsultas = 0, receitaBanhos = 0, receitaVendas = 0;
        decimal receitaMesAtual  = 0, receitaMesPassadoValor = 0;
        var receitaSeisMeses     = new List<decimal>();
        var labelsSeisMeses      = new List<string>();
        var ultimasVendas        = new List<Venda>();

        if (podeFaturamento)
        {
            var tarefaRecConsultas = ExecutarComNovoContextoAsync(db =>
                db.Consulta.AsNoTracking()
                    .Where(c => c.IdEmpresa == idEmpresa
                             && c.DataHora >= inicioMes && c.DataHora <= agora
                             && c.Valor.HasValue)
                    .SumAsync(c => (decimal?)c.Valor));

            var tarefaRecBanhos = ExecutarComNovoContextoAsync(db =>
                db.BanhoTosa.AsNoTracking()
                    .Where(b => b.IdEmpresa == idEmpresa
                             && b.DataHora >= inicioMes && b.DataHora <= agora
                             && b.Valor.HasValue)
                    .SumAsync(b => (decimal?)b.Valor));

            var tarefaRecVendas = ExecutarComNovoContextoAsync(db =>
                db.Venda.AsNoTracking()
                    .Where(v => v.IdEmpresa == idEmpresa && v.DataVenda >= inicioMes)
                    .SumAsync(v => (decimal?)v.Total));

            var tarefaRecMesPassConsultas = ExecutarComNovoContextoAsync(db =>
                db.Consulta.AsNoTracking()
                    .Where(c => c.IdEmpresa == idEmpresa
                             && c.DataHora >= inicioMesPassado && c.DataHora <= fimMesPassado
                             && c.Valor.HasValue)
                    .SumAsync(c => (decimal?)c.Valor));

            var tarefaRecMesPassBanhos = ExecutarComNovoContextoAsync(db =>
                db.BanhoTosa.AsNoTracking()
                    .Where(b => b.IdEmpresa == idEmpresa
                             && b.DataHora >= inicioMesPassado && b.DataHora <= fimMesPassado
                             && b.Valor.HasValue)
                    .SumAsync(b => (decimal?)b.Valor));

            var tarefaRecMesPassVendas = ExecutarComNovoContextoAsync(db =>
                db.Venda.AsNoTracking()
                    .Where(v => v.IdEmpresa == idEmpresa
                             && v.DataVenda >= inicioMesPassado && v.DataVenda <= fimMesPassado)
                    .SumAsync(v => (decimal?)v.Total));

            var tarefaUltimasVendas = ExecutarComNovoContextoAsync(db =>
                db.Venda.AsNoTracking()
                    .Include(v => v.Produto)
                    .Where(v => v.IdEmpresa == idEmpresa)
                    .OrderByDescending(v => v.DataVenda).Take(5).ToListAsync());

            await Task.WhenAll(
                tarefaRecConsultas, tarefaRecBanhos, tarefaRecVendas,
                tarefaRecMesPassConsultas, tarefaRecMesPassBanhos, tarefaRecMesPassVendas,
                tarefaUltimasVendas);

            receitaConsultas       = await tarefaRecConsultas       ?? 0;
            receitaBanhos          = await tarefaRecBanhos          ?? 0;
            receitaVendas          = await tarefaRecVendas          ?? 0;
            receitaMesAtual        = receitaConsultas + receitaBanhos + receitaVendas;
            receitaMesPassadoValor = (await tarefaRecMesPassConsultas ?? 0)
                                   + (await tarefaRecMesPassBanhos    ?? 0)
                                   + (await tarefaRecMesPassVendas    ?? 0);
            ultimasVendas          = await tarefaUltimasVendas;

            // Receita dos últimos 6 meses
            var mesesNomes = new[] { "Jan","Fev","Mar","Abr","Mai","Jun","Jul","Ago","Set","Out","Nov","Dez" };

            var tarefasSeisMeses = Enumerable.Range(0, 6).Reverse().Select(i =>
            {
                var mes           = inicioMes.AddMonths(-i);
                var fimMes        = mes.AddMonths(1).AddDays(-1);
                var limiteDataMes = fimMes > agora ? agora : fimMes;

                var tVendas = ExecutarComNovoContextoAsync(db =>
                    db.Venda.AsNoTracking()
                        .Where(v => v.IdEmpresa == idEmpresa && v.DataVenda >= mes && v.DataVenda <= fimMes)
                        .SumAsync(v => (decimal?)v.Total));

                var tConsultas = ExecutarComNovoContextoAsync(db =>
                    db.Consulta.AsNoTracking()
                        .Where(c => c.IdEmpresa == idEmpresa
                                 && c.DataHora >= mes && c.DataHora <= limiteDataMes
                                 && c.Valor.HasValue)
                        .SumAsync(c => (decimal?)c.Valor));

                var tBanhos = ExecutarComNovoContextoAsync(db =>
                    db.BanhoTosa.AsNoTracking()
                        .Where(b => b.IdEmpresa == idEmpresa
                                 && b.DataHora >= mes && b.DataHora <= limiteDataMes
                                 && b.Valor.HasValue)
                        .SumAsync(b => (decimal?)b.Valor));

                return (mes, tVendas, tConsultas, tBanhos);
            }).ToList();

            await Task.WhenAll(tarefasSeisMeses.SelectMany(t =>
                new Task[] { t.tVendas, t.tConsultas, t.tBanhos }));

            foreach (var (mes, tVendas, tConsultas, tBanhos) in tarefasSeisMeses)
            {
                receitaSeisMeses.Add((await tVendas ?? 0) + (await tConsultas ?? 0) + (await tBanhos ?? 0));
                labelsSeisMeses.Add(mesesNomes[mes.Month - 1]);
            }
        }

        var modelo = new PainelViewModel
        {
            TotalClientes          = await tarefaTotalClientes,
            TotalPets              = await tarefaTotalPets,
            TotalProdutos          = await tarefaTotalProdutos,
            ProdutosSemEstoque     = await tarefaProdutosSemEstoque,
            ConsultasHoje          = await tarefaConsultasHoje,
            BanhosHoje             = await tarefaBanhosHoje,
            ReceitaMesAtual        = receitaMesAtual,
            ReceitaMesPassado      = receitaMesPassadoValor,
            ProximosBanhos         = await tarefaProxBanhos,
            ReceitaSeisMeses       = receitaSeisMeses,
            LabelsSeisMeses        = labelsSeisMeses,
            ReceitaConsultas       = receitaConsultas,
            ReceitaBanhos          = receitaBanhos,
            ReceitaVendas          = receitaVendas,
            VacinasFrascosVencendo = await tarefaVacinasVenc,
            ProximasDoses          = await tarefaProxDoses,
            DosesAtrasadas         = await tarefaDosesAtrasadas,
            UltimasVendas          = ultimasVendas,
            ProximasConsultas      = await tarefaProxConsultas,
        };

        // ─── Salva no cache por 60 segundos ───────────────────────────────
        _cache.Set(chaveCache, modelo, TimeSpan.FromSeconds(60));

        ViewBag.PodeFaturamento = podeFaturamento;
        return View(modelo);
    }

    // ── Helper: executa uma query em uma instância isolada de DbContext ────
    private async Task<T> ExecutarComNovoContextoAsync<T>(Func<Contexto, Task<T>> query)
    {
        await using var db = await _fabricaContexto.CreateDbContextAsync();
        return await query(db);
    }
}
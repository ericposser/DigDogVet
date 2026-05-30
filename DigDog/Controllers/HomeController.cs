using DigDog.Data;
using DigDog.Models;
using DigDog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigDog.Controllers;

[Authorize]
public class HomeController : UtilController
{
    private readonly Contexto _contexto;
    private readonly PermissaoService _permissaoService;
    private readonly IDbContextFactory<Contexto> _fabricaContexto;

    public HomeController(
        Contexto contexto,
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao,
        PermissaoService permissaoService,
        IDbContextFactory<Contexto> fabricaContexto)
        : base(gerenciadorUsuario, provedorProtecao, contexto)
    {
        _contexto         = contexto;
        _permissaoService = permissaoService;
        _fabricaContexto  = fabricaContexto;
    }

    public async Task<IActionResult> Index()
    {
        var idEmpresa        = await ObterIdEmpresaAsync();
        var idUsuario        = ObterIdUsuario();
        var hoje             = DateTime.Today;
        var seteDias         = hoje.AddDays(7);
        var inicioMes        = new DateTime(hoje.Year, hoje.Month, 1);
        var inicioMesPassado = inicioMes.AddMonths(-1);
        var fimMesPassado    = inicioMes.AddDays(-1);

        var agora = DateTime.Now; // capturado uma vez para consistência nas queries de faturamento

        var podeFaturamento = User.IsInRole("Admin") ||
            await _permissaoService.UsuarioTemPermissaoAsync(
                idUsuario, Permissao.PainelFaturamentoVisualizar);

        // ── Contadores e agenda em paralelo ────────────────────────────────
        // Cada tarefa usa sua própria instância de DbContext (via factory)
        // porque o EF Core não é thread-safe e não suporta múltiplas queries
        // simultâneas no mesmo DbContext.

        var totalClientes = await ExecutarComNovoContextoAsync(db =>
            db.Cliente.CountAsync(c => c.IdEmpresa == idEmpresa));

        var totalPets = await ExecutarComNovoContextoAsync(db =>
            db.Pet.CountAsync(p => p.IdEmpresa == idEmpresa));

        var totalProdutos = await ExecutarComNovoContextoAsync(db =>
            db.Produto.CountAsync(p => p.IdEmpresa == idEmpresa));

        var produtosSemEstoque = await ExecutarComNovoContextoAsync(db =>
            db.Produto.CountAsync(p => p.IdEmpresa == idEmpresa && p.Estoque == 0));

        var consultasHoje = await ExecutarComNovoContextoAsync(db =>
            db.Consulta.CountAsync(c => c.IdEmpresa == idEmpresa && c.DataHora.Date == hoje));

        var banhosHoje = await ExecutarComNovoContextoAsync(db =>
            db.BanhoTosa.CountAsync(b => b.IdEmpresa == idEmpresa && b.DataHora.Date == hoje));

        // Agenda e vacinação — paralelizadas com contextos independentes
        var tarefaProxConsultas = ExecutarComNovoContextoAsync(db =>
            db.Consulta
                .Include(c => c.Pet)
                .Where(c => c.IdEmpresa == idEmpresa && c.DataHora >= DateTime.Now)
                .OrderBy(c => c.DataHora).Take(5).ToListAsync());

        var tarefaProxBanhos = ExecutarComNovoContextoAsync(db =>
            db.BanhoTosa
                .Include(b => b.Pet)
                .Where(b => b.IdEmpresa == idEmpresa && b.DataHora >= DateTime.Now)
                .OrderBy(b => b.DataHora).Take(5).ToListAsync());

        var tarefaVacinasVenc = ExecutarComNovoContextoAsync(db =>
            db.Vacina
                .Where(v => v.IdEmpresa == idEmpresa && v.DataValidade >= hoje && v.DataValidade <= seteDias)
                .OrderBy(v => v.DataValidade).ToListAsync());

        var tarefaProxDoses = ExecutarComNovoContextoAsync(db =>
            db.Vacinacao
                .Include(v => v.Pet).Include(v => v.Vacina)
                .Where(v => v.IdEmpresa == idEmpresa
                         && v.DataProximaDose.HasValue
                         && v.DataProximaDose.Value >= hoje
                         && v.DataProximaDose.Value <= seteDias)
                .OrderBy(v => v.DataProximaDose).Take(5).ToListAsync());

        var tarefaDosesAtrasadas = ExecutarComNovoContextoAsync(db =>
            db.Vacinacao
                .Include(v => v.Pet).Include(v => v.Vacina)
                .Where(v => v.IdEmpresa == idEmpresa
                         && v.DataProximaDose.HasValue
                         && v.DataProximaDose.Value < hoje)
                .OrderBy(v => v.DataProximaDose).Take(5).ToListAsync());

        await Task.WhenAll(
            tarefaProxConsultas, tarefaProxBanhos,
            tarefaVacinasVenc, tarefaProxDoses, tarefaDosesAtrasadas);

        // ── Faturamento — queries extras apenas para quem tem permissão ────
        decimal receitaConsultas = 0, receitaBanhos = 0, receitaVendas = 0;
        decimal receitaMesAtual  = 0, receitaMesPassadoValor = 0;
        var receitaSeisMeses     = new List<decimal>();
        var labelsSeisMeses      = new List<string>();
        var ultimasVendas        = new List<Venda>();

        if (podeFaturamento)
        {
            var tarefaRecConsultas = ExecutarComNovoContextoAsync(db =>
                db.Consulta
                    .Where(c => c.IdEmpresa == idEmpresa
                             && c.DataHora >= inicioMes
                             && c.DataHora <= agora       // só consultas que já aconteceram
                             && c.Valor.HasValue)
                    .SumAsync(c => (decimal?)c.Valor));

            var tarefaRecBanhos = ExecutarComNovoContextoAsync(db =>
                db.BanhoTosa
                    .Where(b => b.IdEmpresa == idEmpresa
                             && b.DataHora >= inicioMes
                             && b.DataHora <= agora       // só banhos que já aconteceram
                             && b.Valor.HasValue)
                    .SumAsync(b => (decimal?)b.Valor));

            var tarefaRecVendas = ExecutarComNovoContextoAsync(db =>
                db.Venda
                    .Where(v => v.IdEmpresa == idEmpresa && v.DataVenda >= inicioMes)
                    .SumAsync(v => (decimal?)v.Total));

            var tarefaRecMesPassConsultas = ExecutarComNovoContextoAsync(db =>
                db.Consulta
                    .Where(c => c.IdEmpresa == idEmpresa
                             && c.DataHora >= inicioMesPassado && c.DataHora <= fimMesPassado
                             && c.Valor.HasValue)
                    .SumAsync(c => (decimal?)c.Valor));

            var tarefaRecMesPassBanhos = ExecutarComNovoContextoAsync(db =>
                db.BanhoTosa
                    .Where(b => b.IdEmpresa == idEmpresa
                             && b.DataHora >= inicioMesPassado && b.DataHora <= fimMesPassado
                             && b.Valor.HasValue)
                    .SumAsync(b => (decimal?)b.Valor));

            var tarefaRecMesPassVendas = ExecutarComNovoContextoAsync(db =>
                db.Venda
                    .Where(v => v.IdEmpresa == idEmpresa
                             && v.DataVenda >= inicioMesPassado && v.DataVenda <= fimMesPassado)
                    .SumAsync(v => (decimal?)v.Total));

            var tarefaUltimasVendas = ExecutarComNovoContextoAsync(db =>
                db.Venda
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

            // Receita dos últimos 6 meses — 18 queries paralelizadas
            var mesesNomes = new[] { "Jan","Fev","Mar","Abr","Mai","Jun","Jul","Ago","Set","Out","Nov","Dez" };

            var tarefasSeisMeses = Enumerable.Range(0, 6).Reverse().Select(i =>
            {
                var mes    = inicioMes.AddMonths(-i);
                var fimMes = mes.AddMonths(1).AddDays(-1);

                var tVendas = ExecutarComNovoContextoAsync(db =>
                    db.Venda
                        .Where(v => v.IdEmpresa == idEmpresa && v.DataVenda >= mes && v.DataVenda <= fimMes)
                        .SumAsync(v => (decimal?)v.Total));

                // Para o mês atual, limita a agora (não soma agendamentos futuros)
                // Para meses passados, fimMes já é o último dia do mês
                var limiteDataMes = fimMes > agora ? agora : fimMes;

                var tConsultas = ExecutarComNovoContextoAsync(db =>
                    db.Consulta
                        .Where(c => c.IdEmpresa == idEmpresa
                                 && c.DataHora >= mes
                                 && c.DataHora <= limiteDataMes
                                 && c.Valor.HasValue)
                        .SumAsync(c => (decimal?)c.Valor));

                var tBanhos = ExecutarComNovoContextoAsync(db =>
                    db.BanhoTosa
                        .Where(b => b.IdEmpresa == idEmpresa
                                 && b.DataHora >= mes
                                 && b.DataHora <= limiteDataMes
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
            TotalClientes          = totalClientes,
            TotalPets              = totalPets,
            TotalProdutos          = totalProdutos,
            ProdutosSemEstoque     = produtosSemEstoque,
            ReceitaMesAtual        = receitaMesAtual,
            ReceitaMesPassado      = receitaMesPassadoValor,
            ConsultasHoje          = consultasHoje,
            BanhosHoje             = banhosHoje,
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

        ViewBag.PodeFaturamento = podeFaturamento;
        return View(modelo);
    }

    // ── Helper: executa uma query em uma instância isolada de DbContext ────
    // Necessário porque o DbContext do EF Core não é thread-safe.
    // Cada tarefa paralela precisa da sua própria conexão.
    private async Task<T> ExecutarComNovoContextoAsync<T>(Func<Contexto, Task<T>> query)
    {
        await using var db = await _fabricaContexto.CreateDbContextAsync();
        return await query(db);
    }
}

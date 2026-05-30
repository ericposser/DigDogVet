using DigDog.Data;
using Microsoft.EntityFrameworkCore;

namespace DigDog.Services;

public class LimpezaLogService : BackgroundService
{
    private readonly IServiceScopeFactory _fabricaEscopos;
    private readonly ILogger<LimpezaLogService> _logger;

    private static readonly TimeSpan IntervaloVerificacao = TimeSpan.FromHours(24);

    public LimpezaLogService(
        IServiceScopeFactory fabricaEscopos,
        ILogger<LimpezaLogService> logger)
    {
        _fabricaEscopos = fabricaEscopos;
        _logger         = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken tokenCancelamento)
    {
        _logger.LogInformation("Serviço de limpeza de logs iniciado.");

        while (!tokenCancelamento.IsCancellationRequested)
        {
            try
            {
                await LimparLogsAntigosAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao executar limpeza de logs.");
            }

            await Task.Delay(IntervaloVerificacao, tokenCancelamento);
        }
    }

    private async Task LimparLogsAntigosAsync()
    {
        using var escopo = _fabricaEscopos.CreateScope();
        var contexto     = escopo.ServiceProvider.GetRequiredService<Contexto>();
        var limiteData   = DateTime.Now.AddMonths(-3);

        // ExecuteDeleteAsync gera um DELETE WHERE direto no banco,
        // sem carregar registros em memória — muito mais eficiente
        // quando há grande volume de logs acumulados.
        var quantidade = await contexto.Log
            .Where(l => l.DataHora < limiteData)
            .ExecuteDeleteAsync();

        if (quantidade > 0)
            _logger.LogInformation(
                "Limpeza automática: {Quantidade} log(s) removido(s) com mais de 3 meses.",
                quantidade);
    }
}
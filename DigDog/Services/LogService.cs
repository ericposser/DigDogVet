using DigDog.Data;
using DigDog.Models;

namespace DigDog.Services;

public class LogService
{
    private readonly Contexto _contexto;

    // Limites de caracteres por campo — evitam que payloads abusivos
    // (nomes longos, descrições gigantes) poluam a tabela de auditoria.
    private const int LimiteNomeFuncionario = 150;
    private const int LimiteAcao            = 50;
    private const int LimiteModulo          = 100;
    private const int LimiteDescricao       = 1000;

    public LogService(Contexto contexto)
    {
        _contexto = contexto;
    }

    public async Task RegistrarAsync(
        string nomeFuncionario,
        string acao,
        string modulo,
        string descricao,
        int idEmpresa)
    {
        _contexto.Log.Add(new Log
        {
            NomeFuncionario = Truncar(nomeFuncionario, LimiteNomeFuncionario),
            Acao            = Truncar(acao,            LimiteAcao),
            Modulo          = Truncar(modulo,          LimiteModulo),
            Descricao       = Truncar(descricao,       LimiteDescricao),
            DataHora        = DateTime.Now,
            IdEmpresa       = idEmpresa
        });

        await _contexto.SaveChangesAsync();
    }

    private static string Truncar(string valor, int limite) =>
        valor.Length > limite ? valor[..limite] : valor;
}
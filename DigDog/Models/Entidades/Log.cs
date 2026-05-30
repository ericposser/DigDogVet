using System.ComponentModel.DataAnnotations;

namespace DigDog.Models;

public class Log
{
    public int Id { get; set; }

    public string NomeFuncionario { get; set; } = null!;

    public string Acao { get; set; } = null!;

    public string Modulo { get; set; } = null!;

    public string Descricao { get; set; } = null!;

    public DateTime DataHora { get; set; } = DateTime.Now;

    public int IdEmpresa { get; set; }
    public Empresa Empresa { get; set; } = null!;
}
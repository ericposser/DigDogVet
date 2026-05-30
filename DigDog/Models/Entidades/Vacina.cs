using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace DigDog.Models;

// Catálogo de vacinas da clínica
public class Vacina
{
    [Key]
    public int Id { get; set; }

    [Required(ErrorMessage = "O nome da vacina é obrigatório.")]
    [StringLength(100)]
    [Display(Name = "Nome da Vacina")]
    public string Nome { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Fabricante")]
    public string? Fabricante { get; set; }

    [StringLength(50)]
    [Display(Name = "Lote")]
    public string? Lote { get; set; }

    [Required(ErrorMessage = "A data de validade é obrigatória.")]
    [Display(Name = "Data de Validade")]
    [DataType(DataType.Date)]
    public DateTime DataValidade { get; set; }

    public int IdEmpresa { get; set; }
    public Empresa Empresa { get; set; } = null!;

    // Um tipo de vacina pode ter várias aplicações
    public virtual ICollection<Vacinacao> Vacinacoes { get; set; } = new List<Vacinacao>();
}
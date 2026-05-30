using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace DigDog.Models;

public class Consulta
{
    [Key]
    public int Id { get; set; }

    [Required(ErrorMessage = "A data e hora são obrigatórias.")]
    [Display(Name = "Data e Hora")]
    public DateTime DataHora { get; set; }

    [Required(ErrorMessage = "Descreva o motivo ou sintomas.")]
    [Column(TypeName = "text")]
    [Display(Name = "Motivo / Sintomas")]
    public string Motivo { get; set; } = string.Empty;

    [Column(TypeName = "text")]
    [Display(Name = "Diagnóstico")]
    public string? Diagnostico { get; set; }

    [Column(TypeName = "text")]
    [Display(Name = "Prescrição / Receita")]
    public string? Prescricao { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    [DisplayFormat(DataFormatString = "{0:C2}")]
    [Display(Name = "Valor da Consulta")]
    public decimal? Valor { get; set; }

    // ── Relacionamento: Consulta pertence a um Pet ──────────────────────
    [Required(ErrorMessage = "Selecione o paciente (Pet).")]
    [Display(Name = "Paciente (Pet)")]
    public int IdPet { get; set; }

    [ForeignKey(nameof(IdPet))]
    public virtual Pet? Pet { get; set; }
    // ───────────────────────────────────────────────────────────────────

    public int IdEmpresa { get; set; }
    public Empresa Empresa { get; set; } = null!;
}
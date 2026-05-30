using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace DigDog.Models;

public class BanhoTosa
{
    [Key]
    public int Id { get; set; }

    [Required]
    [Display(Name = "Data e Hora Agendada")]
    public DateTime DataHora { get; set; }

    [Required(ErrorMessage = "Informe o tipo de serviço (ex: Só Banho, Banho e Tosa Máquina).")]
    [StringLength(100)]
    [Display(Name = "Tipo de Serviço")]
    public string TipoServico { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Observações / Restrições (Alergias, agressivo, etc)")]
    public string? Observacoes { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    [DisplayFormat(DataFormatString = "{0:C2}")]
    [Display(Name = "Valor do Serviço")]
    public decimal? Valor { get; set; }

    [Required(ErrorMessage = "Selecione o Pet.")]
    [Display(Name = "Pet")]
    public int IdPet { get; set; }

    [ForeignKey("IdPet")]
    public virtual Pet? Pet { get; set; }
    
    public int IdEmpresa { get; set; }
    public Empresa Empresa { get; set; } = null!;
    
    [Display(Name = "Status")]
    public StatusKanban StatusKanban { get; set; } = StatusKanban.Aguardando;
}
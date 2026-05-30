using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace DigDog.Models;

// Registro de aplicação vinculado ao pet
public class Vacinacao
{
    [Key]
    public int Id { get; set; }

    [StringLength(50)]
    [Display(Name = "Dose")]
    public string? Dose { get; set; } // 1ª Dose, Reforço Anual...

    [Required(ErrorMessage = "A data de aplicação é obrigatória.")]
    [Display(Name = "Data de Aplicação")]
    [DataType(DataType.Date)]
    public DateTime DataAplicacao { get; set; }

    [Display(Name = "Data da Próxima Dose")]
    [DataType(DataType.Date)]
    public DateTime? DataProximaDose { get; set; }

    [StringLength(500)]
    [Display(Name = "Observações")]
    public string? Observacoes { get; set; }

    // Qual vacina foi aplicada
    [Required(ErrorMessage = "Selecione a vacina.")]
    [Display(Name = "Vacina")]
    public int IdVacina { get; set; }

    [ForeignKey(nameof(IdVacina))]
    public virtual Vacina? Vacina { get; set; }

    // Em qual pet foi aplicada
    [Required(ErrorMessage = "Selecione o pet.")]
    [Display(Name = "Pet")]
    public int IdPet { get; set; }

    [ForeignKey(nameof(IdPet))]
    public virtual Pet? Pet { get; set; }

    public int IdEmpresa { get; set; }
    public Empresa Empresa { get; set; } = null!;
}
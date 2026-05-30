using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace DigDog.Models;

public class Venda
{
    [Key]
    public int Id { get; set; }

    [Required]
    [Display(Name = "Data da Venda")]
    public DateTime DataVenda { get; set; } = DateTime.Now;
    
    [Display(Name = "Produto")]
    public int? IdProduto { get; set; }

    [ForeignKey(nameof(IdProduto))]
    public virtual Produto? Produto { get; set; }

    [Required(ErrorMessage = "A quantidade é obrigatória.")]
    [Range(1, int.MaxValue, ErrorMessage = "A quantidade deve ser maior que zero.")]
    [Display(Name = "Quantidade")]
    public int Quantidade { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    [Display(Name = "Preço Unitário")]
    public decimal PrecoUnitario { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    [Display(Name = "Total")]
    public decimal Total { get; set; }

    [StringLength(500)]
    [Display(Name = "Observações")]
    public string? Observacoes { get; set; }

    public int IdEmpresa { get; set; }
    public Empresa Empresa { get; set; } = null!;
}
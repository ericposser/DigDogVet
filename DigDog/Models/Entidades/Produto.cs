using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace DigDog.Models;

public class Produto
{
    [Key]
    public int Id { get; set; }

    [Required(ErrorMessage = "O nome do produto é obrigatório.")]
    [StringLength(150)]
    [Display(Name = "Nome do Produto")]
    public string Nome { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Descrição do Produto")]
    public string? Descricao { get; set; }

    [Required(ErrorMessage = "O preço é obrigatório.")]
    [Column(TypeName = "decimal(10,2)")]
    [DisplayFormat(DataFormatString = "{0:C2}")]
    [Display(Name = "Preço de Venda")]
    public decimal Preco { get; set; }

    [Required]
    [Display(Name = "Quantidade em Estoque")]
    public int Estoque { get; set; }
    
    public int IdEmpresa { get; set; }
    public Empresa Empresa { get; set; } = null!;
    
    public virtual ICollection<Venda> Venda { get; set; } = new List<Venda>();
}
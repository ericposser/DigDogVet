using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace DigDog.Models;

public class Cliente
{
    [Key]
    public int Id { get; set; }

    [Required(ErrorMessage = "O nome do tutor é obrigatório.")]
    [StringLength(100, ErrorMessage = "O nome deve ter no máximo 100 caracteres.")]
    [Display(Name = "Nome do Tutor")]
    public string Nome { get; set; } = string.Empty;

    [StringLength(14)]
    [Display(Name = "CPF")]
    public string? Cpf { get; set; }

    [Required(ErrorMessage = "O telefone é obrigatório.")]
    [StringLength(20)]
    [Display(Name = "Telefone / WhatsApp")]
    public string Telefone { get; set; } = string.Empty;

    [StringLength(100)]
    [EmailAddress(ErrorMessage = "E-mail em formato inválido.")]
    [Display(Name = "E-mail")]
    public string? Email { get; set; }

    [StringLength(200)]
    [Display(Name = "Endereço")]
    public string? Endereco { get; set; }

    // Relacionamento: Um cliente tem vários Pets
    public virtual ICollection<Pet> Pets { get; set; } = new List<Pet>();

    public int IdEmpresa { get; set; }
    public Empresa Empresa { get; set; } = null!;
}
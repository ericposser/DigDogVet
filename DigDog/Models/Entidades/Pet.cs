using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace DigDog.Models;

public class Pet
{
    [Key]
    public int Id { get; set; }

    [Required(ErrorMessage = "O nome do pet é obrigatório.")]
    [StringLength(100)]
    [Display(Name = "Nome do Pet")]
    public string Nome { get; set; } = string.Empty;

    [Required(ErrorMessage = "A espécie é obrigatória (ex: Cachorro, Gato).")]
    [StringLength(50)]
    [Display(Name = "Espécie")]
    public string Especie { get; set; } = string.Empty;

    [StringLength(50)]
    [Display(Name = "Raça")]
    public string? Raca { get; set; }

    [Display(Name = "Data de Nascimento")]
    [DataType(DataType.Date)]
    public DateTime? DataNascimento { get; set; }

    [Required(ErrorMessage = "Selecione o sexo do pet.")]
    [StringLength(10)]
    [Display(Name = "Sexo")]
    public string? Sexo { get; set; } // Macho / Fêmea

    // Relacionamento: Um Pet pertence a um Cliente
    [Required(ErrorMessage = "Selecione o tutor do pet.")]
    [Display(Name = "Tutor")]
    public int IdCliente { get; set; }
    
    [ForeignKey("IdCliente")]
    public virtual Cliente? Cliente { get; set; }
    
    public int IdEmpresa { get; set; }
    public Empresa Empresa { get; set; } = null!;

    public virtual ICollection<Consulta> Consulta { get; set; } = new List<Consulta>();
    public virtual ICollection<BanhoTosa> BanhoTosa { get; set; } = new List<BanhoTosa>();
    public virtual ICollection<Vacina> Vacina { get; set; } = new List<Vacina>();
}
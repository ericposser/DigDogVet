using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace DigDog.Models;

public class Configuracao
{
    [Key]
    public int Id { get; set; }

    [StringLength(150, ErrorMessage = "O nome do estabelecimento deve ter no máximo 150 caracteres.")]
    [Display(Name = "Nome do Estabelecimento")]
    public string NomeEstabelecimento { get; set; } = string.Empty;

    [StringLength(20)]
    [Display(Name = "Telefone")]
    public string? Telefone { get; set; }

    [StringLength(250)]
    [Display(Name = "Endereço")]
    public string? Endereco { get; set; }

    public byte[]? FotoDados { get; set; }

    [StringLength(50)]
    public string? FotoMimeType { get; set; }

    public int IdEmpresa { get; set; }
    public Empresa Empresa { get; set; } = null!;
}
using System.ComponentModel.DataAnnotations;

namespace DigDog.Models;

public class UsuarioFormViewModel
{
    public string? IdUsuario { get; set; }
    
    [Required(ErrorMessage = "O nome é obrigatório.")]
    [MaxLength(100, ErrorMessage = "Máximo de 100 caracteres.")]
    [Display(Name = "Nome")]
    public string Nome { get; set; } = null!;

    [Required(ErrorMessage = "O e-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = null!;

    [Required(ErrorMessage = "Selecione um perfil de acesso.")]
    [Display(Name = "Perfil de Acesso")]
    public string IdRole { get; set; } = null!;
    
    [MaxLength(20, ErrorMessage = "Máximo de 20 caracteres.")]
    [Display(Name = "Telefone")]
    public string? Telefone { get; set; }

    [Required(ErrorMessage = "A senha temporária é obrigatória.")]
    [StringLength(100, MinimumLength = 6,
        ErrorMessage = "A senha deve ter no mínimo {2} caracteres.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha Temporária")]
    public string SenhaTemporaria { get; set; } = null!;

    [DataType(DataType.Password)]
    [Display(Name = "Confirmar Senha")]
    [Compare(nameof(SenhaTemporaria), ErrorMessage = "As senhas não coincidem.")]
    public string? ConfirmarSenha { get; set; }
}
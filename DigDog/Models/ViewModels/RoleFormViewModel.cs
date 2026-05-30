using System.ComponentModel.DataAnnotations;

namespace DigDog.Models;

public class RoleFormViewModel
{
    public string? Id { get; set; }

    [Required(ErrorMessage = "O nome do perfil é obrigatório.")]
    [MaxLength(100, ErrorMessage = "Máximo de 100 caracteres.")]
    [Display(Name = "Nome do Perfil")]
    public string Nome { get; set; } = null!;

    public List<Permissao> PermissoesSelecionadas { get; set; } = new();
    public Dictionary<string, List<PermissaoOpcao>> GruposPermissao { get; set; } = new();
}

public class PermissaoOpcao
{
    public Permissao Valor { get; set; }
    public string Label { get; set; } = null!;
    public bool Selecionada { get; set; }
}
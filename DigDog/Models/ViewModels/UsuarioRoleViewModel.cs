namespace DigDog.Models;

public class UsuarioRoleViewModel
{
    public string IdUsuario { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? Nome      { get; set; }
    public string? NomeRole { get; set; }
    public string? IdRole { get; set; }
    public bool EhAdmin { get; set; }
    public string? Telefone  { get; set; }
}
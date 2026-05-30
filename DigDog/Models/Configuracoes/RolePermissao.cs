namespace DigDog.Models;

public class RolePermissao
{
    public int Id { get; set; }
    public string RoleId { get; set; } = null!;
    public Permissao Permissao { get; set; }
}
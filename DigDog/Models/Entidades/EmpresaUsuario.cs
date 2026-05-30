namespace DigDog.Models;

public class EmpresaUsuario
{
    public int Id { get; set; }

    public int IdEmpresa { get; set; }
    public Empresa Empresa { get; set; } = null!;

    public string IdUsuario { get; set; } = null!;
}
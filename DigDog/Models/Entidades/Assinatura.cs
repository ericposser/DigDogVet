using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DigDog.Models;

public class Assinatura
{
    [Key]
    public int Id { get; set; }
    
    public DateTime DataInicio { get; set; }
    
    public DateTime DataExpiracao { get; set; }
    
    public bool Ativa { get; set; } = true;

    public int IdEmpresa { get; set; }
    public Empresa Empresa { get; set; } = null!;
    
    [NotMapped]
    public bool Expirada => DateTime.Now > DataExpiracao;
    
    [NotMapped]
    public bool EmTeste => (DataExpiracao - DataInicio).TotalDays <= 30;
}
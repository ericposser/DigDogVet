using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace DigDog.Models;

public class CarteiraToken
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Pet ao qual esta carteirinha pertence.
    /// </summary>
    [Required]
    public int IdPet { get; set; }

    [ForeignKey(nameof(IdPet))]
    public virtual Pet? Pet { get; set; }

    /// <summary>
    /// Token público (GUID v4) exposto na URL — não é secret.
    /// </summary>
    [Required]
    [StringLength(36)]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Hash SHA-256 do token — único valor persistido e comparado no servidor.
    /// </summary>
    [Required]
    [StringLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Diferente do KanbanToken, a carteirinha não expira automaticamente.
    /// Só é desativada ao revogar manualmente.
    /// </summary>
    public bool Ativo { get; set; } = true;
    
    public int IdEmpresa { get; set; }
    public Empresa Empresa { get; set; } = null!;
}
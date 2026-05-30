using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace DigDog.Models;

public class KanbanToken
{
    [Key]
    public int Id { get; set; }

    public int IdEmpresa { get; set; }
    public Empresa Empresa { get; set; } = null!;

    /// <summary>
    /// Agendamento específico ao qual este token dá acesso.
    /// Cada tutor recebe um link exclusivo para o pet do seu agendamento.
    /// </summary>
    [Required]
    public int IdBanhoTosa { get; set; }

    [ForeignKey(nameof(IdBanhoTosa))]
    public virtual BanhoTosa? BanhoTosa { get; set; }

    /// <summary>
    /// Token público (GUID v4) exposto na URL — não é secret.
    /// </summary>
    [Required]
    [StringLength(36)]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Hash SHA-256 do token — o que é persistido e comparado no servidor.
    /// </summary>
    [Required]
    [StringLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Expira à meia-noite do dia seguinte à criação (horário do servidor).
    /// </summary>
    public DateTime ExpiraEm { get; set; }

    public bool Ativo { get; set; } = true;
}
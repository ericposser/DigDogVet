using System.ComponentModel.DataAnnotations;

namespace DigDog.Models;

public class TicketViewModel
{
    [Required(ErrorMessage = "O título é obrigatório.")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "O título deve ter entre 3 e 200 caracteres.")]
    [Display(Name = "Título")]
    public string Titulo { get; set; } = string.Empty;

    [Required(ErrorMessage = "A descrição é obrigatória.")]
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "A descrição deve ter entre 10 e 2000 caracteres.")]
    [Display(Name = "Descrição")]
    public string Descricao { get; set; } = string.Empty;

    [Required(ErrorMessage = "Selecione o tipo do ticket.")]
    [Display(Name = "Tipo")]
    public string Tipo { get; set; } = string.Empty;

    [Display(Name = "Imagem (opcional)")]
    public IFormFile? Imagem { get; set; }
}
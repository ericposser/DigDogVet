namespace DigDog.Models;

public class CaktoPayloadViewModel
{
    public string Evento { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Cpf   { get; set; }
}
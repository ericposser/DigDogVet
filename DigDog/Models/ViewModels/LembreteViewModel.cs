namespace DigDog.Models;

/// <summary>
/// Dados necessários para montar a mensagem de lembrete via WhatsApp.
/// </summary>
public class LembreteViewModel
{
    /// <summary>Telefone do tutor (pode conter máscaras: (51) 99999-9999)</summary>
    public string? TelefoneTutor { get; set; }

    /// <summary>Nome completo do tutor</summary>
    public string NomeTutor { get; set; } = string.Empty;

    /// <summary>Nome do pet</summary>
    public string NomePet { get; set; } = string.Empty;

    /// <summary>Tipo de serviço: "consulta" ou "banho e tosa"</summary>
    public string Servico { get; set; } = string.Empty;

    /// <summary>Data e hora do agendamento</summary>
    public DateTime DataHora { get; set; }
}
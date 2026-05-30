using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DigDog.Models;

public enum TipoReceituario
{
    [Display(Name = "Receita Simples")]
    Simples,

    [Display(Name = "Receita de Controle Especial")]
    ControleEspecial
}

public class Receituario
{
    [Key]
    public int Id { get; set; }

    [Required]
    [Display(Name = "Tipo de Receituário")]
    public TipoReceituario Tipo { get; set; } = TipoReceituario.Simples;

    // ── Relacionamentos ───────────────────────────────────────────────────
    public int IdConsulta { get; set; }
    public Consulta Consulta { get; set; } = null!;

    public int IdEmpresa { get; set; }
    public Empresa Empresa { get; set; } = null!;

    // ── Dados da Clínica ──────────────────────────────────────────────────
    [Required(ErrorMessage = "O nome da clínica é obrigatório.")]
    [StringLength(150)]
    [Display(Name = "Nome da Clínica")]
    public string NomeClinica { get; set; } = string.Empty;

    [StringLength(250)]
    [Display(Name = "Endereço da Clínica")]
    public string? EnderecoClinica { get; set; }

    [StringLength(20)]
    [Display(Name = "Telefone da Clínica")]
    public string? TelefoneClinica { get; set; }

    // ── Dados do Veterinário ──────────────────────────────────────────────
    [Required(ErrorMessage = "O nome do veterinário é obrigatório.")]
    [StringLength(150)]
    [Display(Name = "Nome do Veterinário")]
    public string NomeVeterinario { get; set; } = string.Empty;

    [Required(ErrorMessage = "O CRMV é obrigatório.")]
    [StringLength(30)]
    [Display(Name = "CRMV")]
    public string Crmv { get; set; } = string.Empty;

    // ── Dados do Tutor ────────────────────────────────────────────────────
    [Required(ErrorMessage = "O nome do tutor é obrigatório.")]
    [StringLength(150)]
    [Display(Name = "Nome do Tutor")]
    public string NomeTutor { get; set; } = string.Empty;

    [StringLength(14)]
    [Display(Name = "CPF do Tutor")]
    public string? CpfTutor { get; set; }

    [StringLength(250)]
    [Display(Name = "Endereço do Tutor")]
    public string? EnderecoTutor { get; set; }

    // ── Dados do Animal ───────────────────────────────────────────────────
    [Required(ErrorMessage = "O nome do animal é obrigatório.")]
    [StringLength(100)]
    [Display(Name = "Nome do Animal")]
    public string NomeAnimal { get; set; } = string.Empty;

    [StringLength(50)]
    [Display(Name = "Espécie")]
    public string? EspecieAnimal { get; set; }

    [StringLength(50)]
    [Display(Name = "Raça")]
    public string? RacaAnimal { get; set; }

    [StringLength(10)]
    [Display(Name = "Sexo")]
    public string? SexoAnimal { get; set; }

    // ── Medicamentos (serializado como JSON) ──────────────────────────────
    [Required(ErrorMessage = "Inclua ao menos um medicamento.")]
    [Display(Name = "Medicamentos")]
    public string MedicamentosJson { get; set; } = "[]";

    // ── Aparência do PDF ──────────────────────────────────────────────────
    /// <summary>Cor hex dos cabeçalhos das seções no PDF. Ex: #1a3c5e</summary>
    [StringLength(7)]
    [Display(Name = "Cor dos Cabeçalhos")]
    public string CorCabecalho { get; set; } = "#1a3c5e";

    // ── Campos Extras ─────────────────────────────────────────────────────
    [StringLength(1000)]
    [Display(Name = "Observações / Instruções ao Tutor")]
    public string? Observacoes { get; set; }

    [Display(Name = "Data de Emissão")]
    public DateTime DataEmissao { get; set; } = DateTime.Today;

    // ── Propriedade não mapeada ───────────────────────────────────────────
    [NotMapped]
    public List<MedicamentoPrescrito> Medicamentos
    {
        get => System.Text.Json.JsonSerializer
                   .Deserialize<List<MedicamentoPrescrito>>(MedicamentosJson)
               ?? new List<MedicamentoPrescrito>();
        set => MedicamentosJson = System.Text.Json.JsonSerializer.Serialize(value);
    }
}

public class MedicamentoPrescrito
{
    public string NomeMedicamento    { get; set; } = string.Empty;
    public string? Concentracao      { get; set; }
    public string? FormaFarmaceutica { get; set; }
    public string? ViaAdministracao  { get; set; }
    public string? Posologia         { get; set; }
    public string? Quantidade        { get; set; }
}

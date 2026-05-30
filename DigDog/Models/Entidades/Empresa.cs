using System.ComponentModel.DataAnnotations;

namespace DigDog.Models;

public class Empresa
{
    public int Id { get; set; }

    [Required]
    [MaxLength(150)]
    public string NomeEstabelecimento { get; set; } = null!;

    // ID do usuário Admin dono desta empresa
    [Required]
    public string IdAdmin { get; set; } = null!;

    // Navegação
    public ICollection<Cliente> Clientes { get; set; } = new List<Cliente>();
    public ICollection<Pet> Pets { get; set; } = new List<Pet>();
    public ICollection<Consulta> Consultas { get; set; } = new List<Consulta>();
    public ICollection<Vacina> Vacinas { get; set; } = new List<Vacina>();
    public ICollection<Vacinacao> Vacinacoes { get; set; } = new List<Vacinacao>();
    public ICollection<BanhoTosa> BanhosTosas { get; set; } = new List<BanhoTosa>();
    public ICollection<Produto> Produtos { get; set; } = new List<Produto>();
    public ICollection<Venda> Vendas { get; set; } = new List<Venda>();
    public ICollection<EmpresaUsuario> Usuarios { get; set; } = new List<EmpresaUsuario>();
    public Configuracao? Configuracao { get; set; }
    public ICollection<Assinatura> Assinaturas { get; set; } = new List<Assinatura>();
}
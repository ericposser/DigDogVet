namespace DigDog.Models;

public class PainelViewModel
{
    public int TotalClientes { get; set; }
    public int TotalPets { get; set; }
    public int TotalProdutos { get; set; }
    public int ProdutosSemEstoque { get; set; }
    public decimal ReceitaMesAtual { get; set; }
    public decimal ReceitaMesPassado { get; set; }
    public int ConsultasHoje { get; set; }
    public int BanhosHoje { get; set; }

    public List<decimal> ReceitaSeisMeses { get; set; } = new();
    public List<string> LabelsSeisMeses { get; set; } = new();

    public decimal ReceitaConsultas { get; set; }
    public decimal ReceitaBanhos { get; set; }
    public decimal ReceitaVendas { get; set; }

    public List<Vacina> VacinasFrascosVencendo { get; set; } = new();
    public List<Vacinacao> ProximasDoses { get; set; } = new();
    public List<Vacinacao> DosesAtrasadas { get; set; } = new();
    public List<Venda> UltimasVendas { get; set; } = new();
    public List<Consulta> ProximasConsultas { get; set; } = new();
    public List<BanhoTosa> ProximosBanhos { get; set; } = new();

    public decimal VariacaoReceita => ReceitaMesPassado == 0 ? 100 :
        Math.Round((ReceitaMesAtual - ReceitaMesPassado) / ReceitaMesPassado * 100, 1);

    public decimal TotalReceitaMes => ReceitaConsultas + ReceitaBanhos + ReceitaVendas;
}
namespace DigDog.Models;

public enum Permissao
{
    // Painel
    PainelFaturamentoVisualizar,

    // Status do Dia
    StatusDiaVisualizar,
    
    // Tutores
    TutoresVisualizar,
    TutoresCriar,
    TutoresEditar,
    TutoresExcluir,

    // Pets
    PetsVisualizar,
    PetsCriar,
    PetsEditar,
    PetsExcluir,

    // Consultas
    ConsultasVisualizar,
    ConsultasCriar,
    ConsultasEditar,
    ConsultasExcluir,

    // Vacinação
    VacinacaoVisualizar,
    VacinacaoCriar,
    VacinacaoEditar,
    VacinacaoExcluir,

    // Vacinas (catálogo)
    VacinasVisualizar,
    VacinasCriar,
    VacinasEditar,
    VacinasExcluir,

    // Banho & Tosa
    BanhoTosaVisualizar,
    BanhoTosaCriar,
    BanhoTosaEditar,
    BanhoTosaExcluir,

    // Produtos
    ProdutosVisualizar,
    ProdutosCriar,
    ProdutosEditar,
    ProdutosExcluir,

    // Vendas
    VendasVisualizar,
    VendasCriar,
    VendasExcluir,
}
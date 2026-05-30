using DigDog.Data;
using DigDog.Filters;
using DigDog.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DigDog.Controllers;

[Authorize]
public class ReceituarioController : UtilController
{
    // Máximo de medicamentos por receituário — evita payload bomb no gerador de PDF
    private const int MaxMedicamentosPorReceituario = 30;

    private readonly Contexto _contexto;

    public ReceituarioController(
        Contexto contexto,
        UserManager<IdentityUser> gerenciadorUsuario,
        IDataProtectionProvider provedorProtecao)
        : base(gerenciadorUsuario, provedorProtecao, contexto)
    {
        _contexto = contexto;
    }

    // ── GET: exibe o formulário vinculado a uma consulta ──────────────────
    [RequerPermissao(Permissao.ConsultasEditar)]
    public async Task<IActionResult> Criar(string idConsulta)
    {
        var idReal = DescriptografarId(idConsulta);
        if (idReal == null) return NotFound();

        var idEmpresa = await ObterIdEmpresaAsync();

        var consulta = await _contexto.Consulta
            .Include(c => c.Pet)
                .ThenInclude(p => p!.Cliente)
            .Where(c => c.IdEmpresa == idEmpresa)
            .FirstOrDefaultAsync(c => c.Id == idReal);

        if (consulta == null) return NotFound();

        var config  = await _contexto.Configuracao.FirstOrDefaultAsync(c => c.IdEmpresa == idEmpresa);
        var empresa = await _contexto.Empresa.FirstOrDefaultAsync(e => e.Id == idEmpresa);

        var modelo = new Receituario
        {
            IdConsulta      = consulta.Id,
            IdEmpresa       = idEmpresa,
            NomeClinica     = config?.NomeEstabelecimento ?? empresa?.NomeEstabelecimento ?? "DigDogVet",
            TelefoneClinica = config?.Telefone,
            EnderecoClinica = config?.Endereco,
            NomeTutor       = consulta.Pet?.Cliente?.Nome     ?? string.Empty,
            CpfTutor        = consulta.Pet?.Cliente?.Cpf,
            EnderecoTutor   = consulta.Pet?.Cliente?.Endereco ?? string.Empty,
            NomeAnimal      = consulta.Pet?.Nome    ?? string.Empty,
            EspecieAnimal   = consulta.Pet?.Especie,
            RacaAnimal      = consulta.Pet?.Raca,
            SexoAnimal      = consulta.Pet?.Sexo,
            DataEmissao     = DateTime.Today,
            CorCabecalho    = "#1a3c5e",
        };

        ViewBag.LogoBase64 = config?.FotoDados != null
            ? $"data:{config.FotoMimeType};base64,{Convert.ToBase64String(config.FotoDados)}"
            : null;

        ViewBag.IdConsultaCriptografado = idConsulta;
        return View(modelo);
    }

    // ── POST: gera e devolve o PDF para download ──────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequerPermissao(Permissao.ConsultasEditar)]
    public async Task<IActionResult> GerarPdf(
        [FromForm] Receituario modelo,
        [FromForm] List<string> medicamentoNome,
        [FromForm] List<string> medicamentoConcentracao,
        [FromForm] List<string> medicamentoForma,
        [FromForm] List<string> medicamentoVia,
        [FromForm] List<string> medicamentoPosologia,
        [FromForm] List<string> medicamentoQuantidade)
    {
        // Remove validações de campos que não vêm do formulário:
        // Empresa e Consulta são propriedades de navegação (objetos complexos),
        // IdConsulta vem criptografado como hidden field (int não vincula direto),
        // MedicamentosJson é preenchido internamente — nunca pelo form.
        ModelState.Remove(nameof(modelo.Empresa));
        ModelState.Remove(nameof(modelo.Consulta));
        ModelState.Remove(nameof(modelo.IdConsulta));
        ModelState.Remove(nameof(modelo.MedicamentosJson));

        // Limite de medicamentos — protege o gerador de PDF contra listas enormes
        if (medicamentoNome.Count > MaxMedicamentosPorReceituario)
            return BadRequest($"Máximo de {MaxMedicamentosPorReceituario} medicamentos por receituário.");

        // Validação de tamanho dos campos do cabeçalho do receituário
        ValidarTamanhoTexto(nameof(modelo.NomeVeterinario), modelo.NomeVeterinario, 150);
        ValidarTamanhoTexto(nameof(modelo.Crmv),            modelo.Crmv,            30);
        ValidarTamanhoTexto(nameof(modelo.NomeTutor),       modelo.NomeTutor,       150);
        ValidarTamanhoTexto(nameof(modelo.NomeAnimal),      modelo.NomeAnimal,      100);
        ValidarTamanhoTexto(nameof(modelo.Observacoes),     modelo.Observacoes,     1000);

        // Garante que a cor é um hex válido antes de passar ao gerador
        if (string.IsNullOrWhiteSpace(modelo.CorCabecalho) || !modelo.CorCabecalho.StartsWith('#'))
            modelo.CorCabecalho = "#1a3c5e";

        if (!ModelState.IsValid)
            return BadRequest("Dados inválidos no receituário.");

        var medicamentos = new List<MedicamentoPrescrito>();
        for (int i = 0; i < medicamentoNome.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(medicamentoNome[i])) continue;

            medicamentos.Add(new MedicamentoPrescrito
            {
                // Trunca cada campo de medicamento para evitar abuso no PDF
                NomeMedicamento   = Truncar(medicamentoNome[i],                                      150),
                Concentracao      = Truncar(i < medicamentoConcentracao.Count ? medicamentoConcentracao[i] : null, 100),
                FormaFarmaceutica = Truncar(i < medicamentoForma.Count        ? medicamentoForma[i]         : null, 100),
                ViaAdministracao  = Truncar(i < medicamentoVia.Count          ? medicamentoVia[i]           : null, 100),
                Posologia         = Truncar(i < medicamentoPosologia.Count    ? medicamentoPosologia[i]     : null, 500),
                Quantidade        = Truncar(i < medicamentoQuantidade.Count   ? medicamentoQuantidade[i]    : null, 50),
            });
        }

        modelo.Medicamentos = medicamentos;

        var idEmpresa = await ObterIdEmpresaAsync();
        var config    = await _contexto.Configuracao.FirstOrDefaultAsync(c => c.IdEmpresa == idEmpresa);
        byte[]? logoBytes = config?.FotoDados;

        var pdfBytes    = GerarDocumentoPdf(modelo, logoBytes);
        var nomeArquivo = $"Receituario_{modelo.NomeAnimal}_{DateTime.Today:yyyyMMdd}.pdf";

        return File(pdfBytes, "application/pdf", nomeArquivo);
    }

    // ── Geração do PDF ────────────────────────────────────────────────────
    private static byte[] GerarDocumentoPdf(Receituario modelo, byte[]? logoBytes)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var cor      = modelo.CorCabecalho;
        var corFaixa = modelo.Tipo == TipoReceituario.ControleEspecial ? "#8B0000" : cor;
        var textoFaixa = modelo.Tipo == TipoReceituario.ControleEspecial
            ? "RECEITUÁRIO DE CONTROLE ESPECIAL VETERINÁRIO"
            : "RECEITUÁRIO VETERINÁRIO — RECEITA SIMPLES";

        return Document.Create(container =>
        {
            container.Page(pagina =>
            {
                pagina.Size(PageSizes.A4);
                pagina.Margin(1.8f, Unit.Centimetre);
                pagina.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));

                pagina.Header().BorderBottom(1.5f).BorderColor(cor).PaddingBottom(10).Row(linha =>
                {
                    if (logoBytes != null && logoBytes.Length > 0)
                        linha.ConstantItem(65).PaddingRight(10).Image(logoBytes).FitArea();

                    linha.RelativeItem().Column(c =>
                    {
                        c.Item().Text(modelo.NomeClinica).FontSize(14).Bold().FontColor(cor);
                        if (!string.IsNullOrWhiteSpace(modelo.EnderecoClinica))
                            c.Item().PaddingTop(2).Text(modelo.EnderecoClinica)
                                .FontSize(8).FontColor("#555555");
                        if (!string.IsNullOrWhiteSpace(modelo.TelefoneClinica))
                            c.Item().Text($"Tel.: {modelo.TelefoneClinica}")
                                .FontSize(8).FontColor("#555555");
                    });
                });

                pagina.Content().PaddingTop(10).Column(col =>
                {
                    col.Item().Background(corFaixa).Padding(6)
                        .AlignCenter()
                        .Text(textoFaixa).FontSize(10).Bold().FontColor(Colors.White);

                    col.Item().PaddingTop(10).Row(linha =>
                    {
                        linha.RelativeItem().Column(esq =>
                        {
                            SecaoBorda(esq, "DADOS DO PROPRIETÁRIO / TUTOR", cor, corpo =>
                            {
                                CampoTexto(corpo, "Nome Completo:", modelo.NomeTutor);
                                if (!string.IsNullOrWhiteSpace(modelo.CpfTutor))
                                    CampoTexto(corpo, "CPF:", modelo.CpfTutor);
                                if (!string.IsNullOrWhiteSpace(modelo.EnderecoTutor))
                                    CampoTexto(corpo, "Endereço:", modelo.EnderecoTutor);
                            });
                            esq.Item().PaddingTop(8);
                            SecaoBorda(esq, "IDENTIFICAÇÃO DO ANIMAL", cor, corpo =>
                            {
                                CampoTexto(corpo, "Nome:", modelo.NomeAnimal);
                                if (!string.IsNullOrWhiteSpace(modelo.EspecieAnimal))
                                    CampoTexto(corpo, "Espécie:", modelo.EspecieAnimal);
                                if (!string.IsNullOrWhiteSpace(modelo.RacaAnimal))
                                    CampoTexto(corpo, "Raça:", modelo.RacaAnimal);
                                if (!string.IsNullOrWhiteSpace(modelo.SexoAnimal))
                                    CampoTexto(corpo, "Sexo:", modelo.SexoAnimal);
                            });
                        });

                        linha.ConstantItem(12);

                        linha.RelativeItem().Column(dir =>
                        {
                            SecaoBorda(dir, "DADOS DO MÉDICO VETERINÁRIO", cor, corpo =>
                            {
                                CampoTexto(corpo, "Nome:", modelo.NomeVeterinario);
                                CampoTexto(corpo, "CRMV:", modelo.Crmv);
                                if (modelo.Tipo == TipoReceituario.ControleEspecial
                                    && !string.IsNullOrWhiteSpace(modelo.EnderecoClinica))
                                    CampoTexto(corpo, "Endereço:", modelo.EnderecoClinica);
                            });
                            dir.Item().PaddingTop(8);
                            SecaoBorda(dir, "DATA DE EMISSÃO", cor, corpo =>
                            {
                                corpo.Item().Text(
                                    modelo.DataEmissao.ToString(
                                        "dd 'de' MMMM 'de' yyyy",
                                        new System.Globalization.CultureInfo("pt-BR")))
                                    .FontSize(10).Bold();
                            });
                        });
                    });

                    col.Item().PaddingTop(10);
                    SecaoBorda(col, "PRESCRIÇÃO MÉDICA — MEDICAMENTOS", cor, corpo =>
                    {
                        if (modelo.Medicamentos.Count == 0)
                        {
                            corpo.Item().Text("Nenhum medicamento prescrito.")
                                .Italic().FontColor(Colors.Grey.Medium);
                            return;
                        }

                        foreach (var (med, i) in modelo.Medicamentos.Select((m, idx) => (m, idx)))
                        {
                            if (i > 0)
                                corpo.Item().PaddingVertical(4)
                                    .LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);

                            corpo.Item().Row(r =>
                            {
                                r.ConstantItem(18).Text($"{i + 1}.").Bold().FontSize(9);
                                r.RelativeItem().Column(c =>
                                {
                                    c.Item().Text(med.NomeMedicamento).Bold().FontSize(10);
                                    var detalhes = new List<string>();
                                    if (!string.IsNullOrWhiteSpace(med.Concentracao))
                                        detalhes.Add($"Concentração: {med.Concentracao}");
                                    if (!string.IsNullOrWhiteSpace(med.FormaFarmaceutica))
                                        detalhes.Add($"Forma farmacêutica: {med.FormaFarmaceutica}");
                                    if (!string.IsNullOrWhiteSpace(med.ViaAdministracao))
                                        detalhes.Add($"Via de administração: {med.ViaAdministracao}");
                                    if (!string.IsNullOrWhiteSpace(med.Quantidade))
                                        detalhes.Add($"Quantidade: {med.Quantidade}");
                                    if (detalhes.Any())
                                        c.Item().PaddingTop(1)
                                            .Text(string.Join("   |   ", detalhes))
                                            .FontSize(8.5f).FontColor("#444444");
                                    if (!string.IsNullOrWhiteSpace(med.Posologia))
                                        c.Item().PaddingTop(2)
                                            .Text($"Posologia: {med.Posologia}")
                                            .FontSize(9).Italic();
                                });
                            });
                        }
                    });

                    if (!string.IsNullOrWhiteSpace(modelo.Observacoes))
                    {
                        col.Item().PaddingTop(8);
                        SecaoBorda(col, "OBSERVAÇÕES / INSTRUÇÕES AO TUTOR", cor, corpo =>
                        {
                            corpo.Item().Text(modelo.Observacoes).FontSize(9);
                        });
                    }

                    col.Item().PaddingTop(60).AlignCenter().Width(240).Column(ass =>
                    {
                        ass.Item().LineHorizontal(1).LineColor(Colors.Black);
                        ass.Item().PaddingTop(6).AlignCenter()
                            .Text(modelo.NomeVeterinario).FontSize(9).Bold();
                        ass.Item().AlignCenter()
                            .Text($"CRMV {modelo.Crmv}").FontSize(8.5f).FontColor("#555555");
                        ass.Item().AlignCenter()
                            .Text("Assinatura do Médico Veterinário")
                            .FontSize(8).Italic().FontColor("#888888");
                    });

                    if (modelo.Tipo == TipoReceituario.ControleEspecial)
                    {
                        col.Item().PaddingTop(20).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                        col.Item().PaddingTop(6).Row(r =>
                        {
                            r.RelativeItem().AlignCenter()
                                .Text("1ª Via — Estabelecimento Dispensador").FontSize(7.5f).Italic();
                            r.RelativeItem().AlignCenter()
                                .Text("2ª Via — Comprador").FontSize(7.5f).Italic();
                            r.RelativeItem().AlignCenter()
                                .Text("3ª Via — Prescritor").FontSize(7.5f).Italic();
                        });
                    }
                });

                pagina.Footer().Column(rod =>
                {
                    rod.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                    rod.Item().PaddingTop(4).Row(r =>
                    {
                        r.RelativeItem()
                            .Text($"Emitido por {modelo.NomeClinica} em {modelo.DataEmissao:dd/MM/yyyy}")
                            .FontSize(7).FontColor(Colors.Grey.Darken1);
                        r.RelativeItem().AlignRight().Text(x =>
                        {
                            x.Span("Página ").FontSize(7).FontColor(Colors.Grey.Darken1);
                            x.CurrentPageNumber().FontSize(7).FontColor(Colors.Grey.Darken1);
                            x.Span(" de ").FontSize(7).FontColor(Colors.Grey.Darken1);
                            x.TotalPages().FontSize(7).FontColor(Colors.Grey.Darken1);
                        });
                    });
                });
            });
        }).GeneratePdf();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Trunca um campo de texto ao limite indicado.
    /// Usado nos campos dos medicamentos antes de passar ao gerador de PDF,
    /// garantindo que nenhum campo abusivamente longo seja processado.
    /// </summary>
    private static string? Truncar(string? valor, int limite) =>
        valor != null && valor.Length > limite ? valor[..limite] : valor;

    private static void SecaoBorda(ColumnDescriptor coluna, string titulo, string cor, Action<ColumnDescriptor> conteudo)
    {
        coluna.Item()
            .Border(0.5f).BorderColor(Colors.Grey.Lighten1)
            .Column(sec =>
            {
                sec.Item().Background(cor).Padding(4)
                    .Text(titulo).FontSize(7.5f).Bold().FontColor(Colors.White);
                sec.Item().Padding(6).Column(corpo => conteudo(corpo));
            });
    }

    private static void CampoTexto(ColumnDescriptor coluna, string rotulo, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return;
        coluna.Item().PaddingBottom(2).Row(r =>
        {
            r.ConstantItem(88).Text(rotulo).Bold().FontSize(8.5f).FontColor("#333333");
            r.RelativeItem().Text(valor).FontSize(8.5f);
        });
    }
}

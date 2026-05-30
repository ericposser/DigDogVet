using Microsoft.Extensions.Configuration;

namespace DigDog.Services;

public class TrelloService
{
    private readonly HttpClient _httpCliente;
    private readonly string _chaveApi;
    private readonly string _token;
    private readonly string _idLista;
    private readonly string _idQuadro;

    private const string UrlBase = "https://api.trello.com/1";

    public TrelloService(HttpClient httpCliente, IConfiguration configuracao)
    {
        _httpCliente = httpCliente;

        // Valida no construtor — falha rápido em vez de lançar exceção obscura
        // durante uma chamada à API em produção.
        _chaveApi = configuracao["Trello:ChaveApi"]
            ?? throw new InvalidOperationException("Configuração 'Trello:ChaveApi' não encontrada.");
        _token    = configuracao["Trello:Token"]
            ?? throw new InvalidOperationException("Configuração 'Trello:Token' não encontrada.");
        _idLista  = configuracao["Trello:IdLista"]
            ?? throw new InvalidOperationException("Configuração 'Trello:IdLista' não encontrada.");
        _idQuadro = configuracao["Trello:IdQuadro"]
            ?? throw new InvalidOperationException("Configuração 'Trello:IdQuadro' não encontrada.");
    }

    public async Task<string?> CriarCardAsync(
        string titulo, string descricao, string tipo, string emailUsuario)
    {
        var prefixoTipo    = tipo == "erro" ? "[Erro]" : "[Melhoria]";
        var tituloCompleto = $"{prefixoTipo} {titulo}";
        var dataCriacao    = DateTime.Now.ToString("dd/MM/yyyy 'às' HH:mm");

        var descricaoCompleta = $"""
            📅 **Criado em:** {dataCriacao}
            👤 **Usuário:** {emailUsuario}

            ---

            {descricao}
            """;

        var idEtiqueta = await ObterOuCriarEtiquetaAsync(tipo);

        var parametros = new Dictionary<string, string>
        {
            { "name",   tituloCompleto    },
            { "desc",   descricaoCompleta },
            { "idList", _idLista          },
            { "key",    _chaveApi         },
            { "token",  _token            }
        };

        if (idEtiqueta != null)
            parametros.Add("idLabels", idEtiqueta);

        var resposta = await _httpCliente.PostAsync(
            $"{UrlBase}/cards",
            new FormUrlEncodedContent(parametros));

        if (!resposta.IsSuccessStatusCode)
            return null;

        var json = await resposta.Content.ReadAsStringAsync();
        return ExtrairValorJson(json, "id");
    }

    public async Task<bool> AnexarImagemAsync(string idCard, IFormFile imagem)
    {
        await using var stream = imagem.OpenReadStream();
        using var conteudo     = new MultipartFormDataContent();

        conteudo.Add(new StringContent(_chaveApi), "key");
        conteudo.Add(new StringContent(_token),    "token");

        var arquivoConteudo = new StreamContent(stream);
        arquivoConteudo.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue(imagem.ContentType);

        // Sanitiza o nome do arquivo antes de enviar no header multipart.
        // Remove caracteres que poderiam quebrar o Content-Disposition ou
        // permitir path traversal no servidor do Trello.
        var nomeArquivoSeguro = SanitizarNomeArquivo(imagem.FileName);

        conteudo.Add(arquivoConteudo, "file", nomeArquivoSeguro);

        var resposta = await _httpCliente.PostAsync(
            $"{UrlBase}/cards/{idCard}/attachments",
            conteudo);

        return resposta.IsSuccessStatusCode;
    }

    // ── Helpers privados ───────────────────────────────────────────────────

    private async Task<string?> ObterOuCriarEtiquetaAsync(string tipo)
    {
        var nomeEtiqueta = tipo == "erro" ? "Erro" : "Melhoria";
        var corEtiqueta  = tipo == "erro" ? "red"  : "blue";

        var urlListar      = $"{UrlBase}/boards/{_idQuadro}/labels?key={_chaveApi}&token={_token}";
        var respostaListar = await _httpCliente.GetAsync(urlListar);

        if (respostaListar.IsSuccessStatusCode)
        {
            var json        = await respostaListar.Content.ReadAsStringAsync();
            var idExistente = BuscarEtiquetaNoJson(json, nomeEtiqueta);
            if (idExistente != null)
                return idExistente;
        }

        var parametrosCriar = new Dictionary<string, string>
        {
            { "name",    nomeEtiqueta },
            { "color",   corEtiqueta  },
            { "idBoard", _idQuadro    },
            { "key",     _chaveApi    },
            { "token",   _token       }
        };

        var respostaCriar = await _httpCliente.PostAsync(
            $"{UrlBase}/labels",
            new FormUrlEncodedContent(parametrosCriar));

        if (!respostaCriar.IsSuccessStatusCode)
            return null;

        var jsonCriado = await respostaCriar.Content.ReadAsStringAsync();
        return ExtrairValorJson(jsonCriado, "id");
    }

    /// <summary>
    /// Remove caracteres inválidos ou perigosos do nome do arquivo antes de
    /// enviá-lo no header Content-Disposition do multipart.
    /// Mantém apenas letras, dígitos, hífen, underscore e ponto.
    /// Garante extensão preservada e comprimento máximo de 100 caracteres.
    /// </summary>
    private static string SanitizarNomeArquivo(string nomeOriginal)
    {
        if (string.IsNullOrWhiteSpace(nomeOriginal))
            return "arquivo";

        // Extrai apenas o nome do arquivo (descarta path caso venha com barras)
        var nome = Path.GetFileNameWithoutExtension(nomeOriginal);
        var ext  = Path.GetExtension(nomeOriginal);

        // Mantém apenas caracteres seguros
        var nomeLimpo = new string(nome
            .Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')
            .ToArray());

        if (string.IsNullOrWhiteSpace(nomeLimpo))
            nomeLimpo = "arquivo";

        // Limita o comprimento total
        const int limiteNome = 90;
        if (nomeLimpo.Length > limiteNome)
            nomeLimpo = nomeLimpo[..limiteNome];

        // Sanitiza a extensão também
        var extLimpa = new string(ext
            .Where(c => char.IsLetterOrDigit(c) || c == '.')
            .ToArray());

        return $"{nomeLimpo}{extLimpa}";
    }

    private static string? BuscarEtiquetaNoJson(string json, string nome)
    {
        var marcadorNome = $"\"name\":\"{nome}\"";
        var posNome      = json.IndexOf(marcadorNome, StringComparison.OrdinalIgnoreCase);
        if (posNome < 0) return null;

        var inicioObjeto = json.LastIndexOf('{', posNome);
        if (inicioObjeto < 0) return null;

        var trecho = json[inicioObjeto..posNome];
        return ExtrairValorJson(trecho, "id");
    }

    private static string? ExtrairValorJson(string json, string chave)
    {
        var marcador = $"\"{chave}\":\"";
        var inicio   = json.IndexOf(marcador, StringComparison.Ordinal);
        if (inicio < 0) return null;
        inicio += marcador.Length;
        var fim = json.IndexOf('"', inicio);
        return fim < 0 ? null : json[inicio..fim];
    }
}

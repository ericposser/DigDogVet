using System.Text;
using DigDog.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DigDog.Controllers;

[AllowAnonymous]
[ApiController]
[Route("[controller]")]
[EnableRateLimiting("webhook")]
public class WebhookController : ControllerBase
{
    // Tamanho máximo aceito para o body do webhook: 1 MB
    // Protege contra payload bomb — antes de qualquer processamento.
    private const int TamanhoMaximoBodyBytes = 1 * 1024 * 1024;

    private readonly CaktoWebhookService _caktoService;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(CaktoWebhookService caktoService, ILogger<WebhookController> logger)
    {
        _caktoService = caktoService;
        _logger       = logger;
    }

    [HttpPost("Cakto")]
    public async Task<IActionResult> Cakto()
    {
        // Rejeita bodies acima do limite antes de ler o stream
        if (Request.ContentLength.HasValue && Request.ContentLength.Value > TamanhoMaximoBodyBytes)
        {
            _logger.LogWarning("Webhook Cakto: body rejeitado por exceder {Limite} bytes.", TamanhoMaximoBodyBytes);
            return StatusCode(StatusCodes.Status413RequestEntityTooLarge);
        }

        string bodyBruto;
        using (var leitor = new StreamReader(
            Request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true))
        {
            // Leitura com limite explícito — defesa secundária caso ContentLength não venha
            var buffer = new char[TamanhoMaximoBodyBytes / 2]; // chars são 2 bytes
            var lidos  = await leitor.ReadBlockAsync(buffer, 0, buffer.Length);
            bodyBruto  = new string(buffer, 0, lidos);
        }

        if (string.IsNullOrWhiteSpace(bodyBruto))
        {
            _logger.LogWarning("Webhook Cakto: body vazio.");
            return BadRequest("Body vazio.");
        }

        // Valida assinatura HMAC antes de qualquer processamento do payload
        if (!_caktoService.AssinaturaValida(Request.Headers, bodyBruto))
        {
            _logger.LogWarning("Webhook Cakto: assinatura inválida.");
            return Unauthorized("Assinatura inválida.");
        }

        try
        {
            var payload = _caktoService.ExtrairPayload(bodyBruto);
            _logger.LogInformation("Webhook Cakto recebido: {Evento}", payload.Evento);
            await _caktoService.ProcessarEventoAsync(payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar webhook Cakto.");
            // Retorna 500 sem expor detalhes internos
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok();
    }
}

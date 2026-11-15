using Api.Contracts;
using Infrastructure.Payments;
using Microsoft.Extensions.Options;
using Application.Payments.Commands;
using Application.Payments.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/payments")]
[Route("payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly MercadoPagoOptions _mercadoPago;

    public PaymentsController(ISender sender, IOptions<MercadoPagoOptions> mercadoPago)
    {
        _sender = sender;
        _mercadoPago = mercadoPago.Value;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var paymentId = await _sender.Send(new CreatePaymentCommand(request.BudgetId, request.Amount), cancellationToken);
        return Created($"/api/payments/{paymentId}", new { id = paymentId });
    }

    [HttpGet("{paymentId:guid}")]
    public async Task<ActionResult<PaymentResponse>> GetById([FromRoute] Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await _sender.Send(new GetPaymentQuery(paymentId), cancellationToken);
        return Ok(payment);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<PaymentSummaryResponse>>> List(CancellationToken cancellationToken)
    {
        var payments = await _sender.Send(new ListPaymentsQuery(), cancellationToken);
        return Ok(payments);
    }

    [HttpPost("{paymentId:guid}/approve")]
    public async Task<IActionResult> Approve(
        [FromRoute] Guid paymentId,
        [FromBody] ApprovePaymentRequest request,
        CancellationToken cancellationToken)
    {
        await _sender.Send(new ApprovePaymentCommand(paymentId, request.SimulateFailure), cancellationToken);
        return NoContent();
    }

    [HttpPost("{paymentId:guid}/reject")]
    public async Task<IActionResult> Reject(
        [FromRoute] Guid paymentId,
        [FromBody] RejectPaymentRequest request,
        CancellationToken cancellationToken)
    {
        await _sender.Send(new RejectPaymentCommand(paymentId, request.Reason), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Confere no gateway se o cliente já pagou o checkout e aprova ou recusa o pagamento. Útil quando o webhook
    /// não alcança o serviço (sem URL pública).
    /// </summary>
    [HttpPost("{paymentId:guid}/sync")]
    public async Task<IActionResult> Sync([FromRoute] Guid paymentId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new ConfirmGatewayPaymentCommand(null, paymentId), cancellationToken);
        if (result == ConfirmGatewayPaymentResult.NotFound)
        {
            return NotFound(new { error = $"No gateway payment found for payment '{paymentId}'." });
        }

        return Ok(await _sender.Send(new GetPaymentQuery(paymentId), cancellationToken));
    }

    /// <summary>
    /// Webhook do Mercado Pago (notificação de pagamento). Responde 200 para tudo que não é pagamento, para o
    /// Mercado Pago não ficar reenviando.
    /// </summary>
    [HttpPost("webhooks/mercadopago")]
    public async Task<IActionResult> MercadoPagoWebhook(
        [FromBody] MercadoPagoNotification? notification,
        [FromQuery(Name = "data.id")] string? queryDataId,
        [FromQuery(Name = "type")] string? queryType,
        [FromQuery(Name = "topic")] string? queryTopic,
        [FromQuery(Name = "id")] string? queryId,
        CancellationToken cancellationToken)
    {
        var type = notification?.Type ?? queryType ?? queryTopic;
        var dataId = notification?.Data?.Id ?? queryDataId ?? queryId;

        if (!string.IsNullOrWhiteSpace(_mercadoPago.WebhookSecret) &&
            !MercadoPagoWebhookSignature.IsValid(
                Request.Headers["x-signature"].FirstOrDefault(),
                Request.Headers["x-request-id"].FirstOrDefault(),
                queryDataId ?? dataId,
                _mercadoPago.WebhookSecret))
        {
            return Unauthorized();
        }

        if (!string.Equals(type, "payment", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(dataId))
        {
            return Ok();
        }

        var result = await _sender.Send(new ConfirmGatewayPaymentCommand(dataId, null), cancellationToken);
        return Ok(new { result = result.ToString() });
    }
}

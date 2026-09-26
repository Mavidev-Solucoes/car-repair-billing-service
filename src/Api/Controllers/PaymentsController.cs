using Api.Contracts;
using Application.Payments.Commands;
using Application.Payments.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly ISender _sender;

    public PaymentsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var paymentId = await _sender.Send(new CreatePaymentCommand(request.BudgetId, request.Amount), cancellationToken);
        return Created($"/payments/{paymentId}", new { id = paymentId });
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
}

using Api.Contracts;
using Application.Payments.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/payments")]
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
        return CreatedAtAction(nameof(Create), new { id = paymentId }, new { id = paymentId });
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

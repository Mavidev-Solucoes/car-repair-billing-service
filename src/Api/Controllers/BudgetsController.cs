using Api.Contracts;
using Application.Budgets.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/budgets")]
public sealed class BudgetsController : ControllerBase
{
    private readonly ISender _sender;

    public BudgetsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBudgetRequest request, CancellationToken cancellationToken)
    {
        var budgetId = await _sender.Send(
            new CreateBudgetCommand(
                request.CustomerName,
                request.Items.Select(x => new CreateBudgetItemModel(x.Description, x.UnitPrice, x.Quantity)).ToList()),
            cancellationToken);

        return CreatedAtAction(nameof(Create), new { id = budgetId }, new { id = budgetId });
    }

    [HttpPost("{budgetId:guid}/approve")]
    public async Task<IActionResult> Approve([FromRoute] Guid budgetId, CancellationToken cancellationToken)
    {
        await _sender.Send(new ApproveBudgetCommand(budgetId), cancellationToken);
        return NoContent();
    }

    [HttpPost("{budgetId:guid}/reject")]
    public async Task<IActionResult> Reject(
        [FromRoute] Guid budgetId,
        [FromBody] RejectBudgetRequest request,
        CancellationToken cancellationToken)
    {
        await _sender.Send(new RejectBudgetCommand(budgetId, request.Reason), cancellationToken);
        return NoContent();
    }
}

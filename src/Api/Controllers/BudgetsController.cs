using Api.Contracts;
using Application.Budgets.Commands;
using Application.Budgets.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("budgets")]
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

        return Created($"/budgets/{budgetId}", new { id = budgetId });
    }

    [HttpGet("{budgetId:guid}")]
    public async Task<ActionResult<BudgetResponse>> GetById([FromRoute] Guid budgetId, CancellationToken cancellationToken)
    {
        var budget = await _sender.Send(new GetBudgetQuery(budgetId), cancellationToken);
        return Ok(budget);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<BudgetSummaryResponse>>> List(CancellationToken cancellationToken)
    {
        var budgets = await _sender.Send(new ListBudgetsQuery(), cancellationToken);
        return Ok(budgets);
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

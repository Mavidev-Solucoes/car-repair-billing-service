using Domain.Enums;

namespace Application.Budgets.Queries;

public sealed record BudgetResponse(
    Guid Id,
    Guid ServiceOrderId,
    string CustomerName,
    BudgetStatus Status,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    DateTime? ApprovedAtUtc,
    DateTime? RejectedAtUtc,
    string? RejectionReason,
    DateTime? CancelledAtUtc,
    IReadOnlyCollection<BudgetItemResponse> Items);

public sealed record BudgetItemResponse(
    Guid Id,
    string Description,
    decimal UnitPrice,
    int Quantity,
    decimal Total);

public sealed record BudgetSummaryResponse(
    Guid Id,
    Guid ServiceOrderId,
    string CustomerName,
    BudgetStatus Status,
    decimal TotalAmount,
    DateTime CreatedAtUtc);

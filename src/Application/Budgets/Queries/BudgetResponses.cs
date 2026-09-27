using Domain.Enums;

namespace Application.Budgets.Queries;

public sealed record BudgetResponse(
    Guid Id,
    string CustomerName,
    BudgetStatus Status,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    DateTime? ApprovedAtUtc,
    DateTime? RejectedAtUtc,
    string? RejectionReason,
    IReadOnlyCollection<BudgetItemResponse> Items);

public sealed record BudgetItemResponse(
    Guid Id,
    string Description,
    decimal UnitPrice,
    int Quantity,
    decimal Total);

public sealed record BudgetSummaryResponse(
    Guid Id,
    string CustomerName,
    BudgetStatus Status,
    decimal TotalAmount,
    DateTime CreatedAtUtc);

using Domain.Enums;

namespace Application.Payments.Queries;

public sealed record PaymentResponse(
    Guid Id,
    Guid BudgetId,
    decimal Amount,
    PaymentStatus Status,
    DateTime CreatedAtUtc,
    DateTime? ApprovedAtUtc,
    DateTime? RejectedAtUtc,
    string? RejectionReason,
    IReadOnlyCollection<PaymentTransactionResponse> Transactions);

public sealed record PaymentTransactionResponse(
    Guid Id,
    bool IsSuccess,
    string ExternalReference,
    string Message,
    DateTime CreatedAtUtc);

public sealed record PaymentSummaryResponse(
    Guid Id,
    Guid BudgetId,
    decimal Amount,
    PaymentStatus Status,
    DateTime CreatedAtUtc);

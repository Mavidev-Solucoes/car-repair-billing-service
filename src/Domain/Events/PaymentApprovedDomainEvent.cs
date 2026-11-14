using Domain.Common;

namespace Domain.Events;

public sealed record PaymentApprovedDomainEvent(Guid PaymentId, Guid BudgetId, Guid ServiceOrderId, decimal Amount) : IServiceOrderEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

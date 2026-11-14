using Domain.Common;

namespace Domain.Events;

public sealed record BudgetCancelledDomainEvent(Guid BudgetId, Guid ServiceOrderId, IReadOnlyCollection<Guid> RefundedPaymentIds) : IServiceOrderEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

using Domain.Common;

namespace Domain.Events;

public sealed record PaymentRejectedDomainEvent(Guid PaymentId, Guid BudgetId, Guid ServiceOrderId, string Reason) : IServiceOrderEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

using Domain.Common;

namespace Domain.Events;

public sealed record PaymentCreatedDomainEvent(Guid PaymentId, Guid BudgetId, Guid ServiceOrderId) : IServiceOrderEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

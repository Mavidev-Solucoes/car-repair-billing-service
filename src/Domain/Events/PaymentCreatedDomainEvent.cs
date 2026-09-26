using Domain.Common;

namespace Domain.Events;

public sealed record PaymentCreatedDomainEvent(Guid PaymentId, Guid BudgetId) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

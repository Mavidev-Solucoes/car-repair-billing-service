using Domain.Common;

namespace Domain.Events;

public sealed record BudgetCreatedDomainEvent(Guid BudgetId) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

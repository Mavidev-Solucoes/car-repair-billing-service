using Domain.Common;

namespace Domain.Events;

public sealed record BudgetApprovedDomainEvent(Guid BudgetId) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

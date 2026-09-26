using Domain.Common;

namespace Domain.Events;

public sealed record BudgetRejectedDomainEvent(Guid BudgetId, string Reason) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

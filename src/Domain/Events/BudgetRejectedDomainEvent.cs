using Domain.Common;

namespace Domain.Events;

public sealed record BudgetRejectedDomainEvent(Guid BudgetId, Guid ServiceOrderId, string Reason) : IServiceOrderEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

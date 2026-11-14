using Domain.Common;

namespace Domain.Events;

public sealed record BudgetCreatedDomainEvent(Guid BudgetId, Guid ServiceOrderId, decimal TotalAmount) : IServiceOrderEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

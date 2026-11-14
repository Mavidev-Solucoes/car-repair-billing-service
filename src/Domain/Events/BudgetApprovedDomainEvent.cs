using Domain.Common;

namespace Domain.Events;

public sealed record BudgetApprovedDomainEvent(Guid BudgetId, Guid ServiceOrderId, decimal TotalAmount, bool SimulatePaymentFailure) : IServiceOrderEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

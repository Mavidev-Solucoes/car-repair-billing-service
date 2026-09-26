using Domain.Common;

namespace Domain.Events;

public sealed record PaymentApprovedDomainEvent(Guid PaymentId) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

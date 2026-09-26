using Domain.Common;

namespace Domain.Events;

public sealed record PaymentRejectedDomainEvent(Guid PaymentId, string Reason) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

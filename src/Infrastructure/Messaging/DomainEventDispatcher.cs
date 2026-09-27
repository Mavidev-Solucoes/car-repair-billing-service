using Application.Abstractions.Messaging;
using Domain.Common;
using Domain.Events;

namespace Infrastructure.Messaging;

public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IEventPublisher _eventPublisher;

    public DomainEventDispatcher(IEventPublisher eventPublisher)
    {
        _eventPublisher = eventPublisher;
    }

    public async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
        {
            switch (domainEvent)
            {
                case BudgetCreatedDomainEvent budgetCreated:
                    await PublishAsync(budgetCreated, "budgets.created", cancellationToken);
                    break;
                case BudgetApprovedDomainEvent budgetApproved:
                    await PublishAsync(budgetApproved, "budgets.approved", cancellationToken);
                    break;
                case BudgetRejectedDomainEvent budgetRejected:
                    await PublishAsync(budgetRejected, "budgets.rejected", cancellationToken);
                    break;
                case PaymentCreatedDomainEvent paymentCreated:
                    await PublishAsync(paymentCreated, "payments.created", cancellationToken);
                    break;
                case PaymentApprovedDomainEvent paymentApproved:
                    await PublishAsync(paymentApproved, "payments.approved", cancellationToken);
                    break;
                case PaymentRejectedDomainEvent paymentRejected:
                    await PublishAsync(paymentRejected, "payments.rejected", cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported domain event type '{domainEvent.GetType().FullName}'.");
            }
        }
    }

    private Task PublishAsync<TDomainEvent>(TDomainEvent domainEvent, string routingKey, CancellationToken cancellationToken)
        where TDomainEvent : class, IDomainEvent
    {
        var envelope = new MessageEnvelope<TDomainEvent>(
            MessageId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            SagaId: null,
            EventType: typeof(TDomainEvent).Name,
            EventVersion: 1,
            OccurredAt: domainEvent.OccurredOnUtc,
            Payload: domainEvent);

        return _eventPublisher.PublishAsync(envelope, routingKey, cancellationToken);
    }
}

namespace Application.Abstractions.Messaging;

public interface IEventPublisher
{
    Task PublishAsync(IntegrationMessage message, CancellationToken cancellationToken = default);
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyCollection<Domain.Common.IDomainEvent> domainEvents, CancellationToken cancellationToken);
}

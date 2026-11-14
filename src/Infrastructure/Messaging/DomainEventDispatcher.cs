using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Abstractions.Messaging;
using Domain.Common;

namespace Infrastructure.Messaging;

/// <summary>
/// Publica os eventos de domínio no envelope do contrato: tipo = nome da classe sem "DomainEvent",
/// correlationId = id da OS.
/// </summary>
public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private const string DomainEventSuffix = "DomainEvent";

    private readonly IEventPublisher _eventPublisher;

    public DomainEventDispatcher(IEventPublisher eventPublisher)
    {
        _eventPublisher = eventPublisher;
    }

    public async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
        {
            await _eventPublisher.PublishAsync(ToIntegrationMessage(domainEvent), cancellationToken);
        }
    }

    public static IntegrationMessage ToIntegrationMessage(IDomainEvent domainEvent)
    {
        if (domainEvent is not IServiceOrderEvent serviceOrderEvent)
        {
            throw new InvalidOperationException(
                $"Domain event '{domainEvent.GetType().FullName}' is not linked to a service order.");
        }

        var payload = JsonSerializer.SerializeToNode(domainEvent, domainEvent.GetType(), IntegrationMessage.SerializerOptions) as JsonObject ?? [];
        payload.Remove("occurredOnUtc");

        var name = domainEvent.GetType().Name;
        return new IntegrationMessage
        {
            Type = name.EndsWith(DomainEventSuffix, StringComparison.Ordinal) ? name[..^DomainEventSuffix.Length] : name,
            CorrelationId = serviceOrderEvent.ServiceOrderId.ToString("D"),
            OccurredAtUtc = domainEvent.OccurredOnUtc,
            Payload = payload
        };
    }
}

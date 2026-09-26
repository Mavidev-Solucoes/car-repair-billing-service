namespace Application.Abstractions.Messaging;

public sealed record MessageEnvelope<TMessage>(
    Guid MessageId,
    Guid CorrelationId,
    Guid? SagaId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAt,
    TMessage Payload)
    where TMessage : class;

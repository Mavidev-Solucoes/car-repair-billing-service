namespace Application.Abstractions.Messaging;

public interface ICommandConsumer
{
    Task SubscribeAsync<TCommand>(
        string queueName,
        string routingKey,
        Func<MessageEnvelope<TCommand>, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
        where TCommand : class;
}

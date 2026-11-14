namespace Application.Abstractions.Messaging;

public interface ICommandConsumer
{
    Task SubscribeAsync(
        string queueName,
        string commandType,
        Func<IntegrationMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default);
}

using System.Text;
using System.Text.Json;
using Application.Abstractions.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Infrastructure.Messaging;

public sealed class RabbitMqEventPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly string _exchangeName;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private readonly IConnection _connection;
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    public RabbitMqEventPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqEventPublisher> logger)
    {
        var rabbitMqOptions = options.Value;
        _exchangeName = rabbitMqOptions.ExchangeName;
        _logger = logger;

        var factory = new ConnectionFactory
        {
            HostName = rabbitMqOptions.HostName,
            Port = rabbitMqOptions.Port,
            UserName = rabbitMqOptions.UserName,
            Password = rabbitMqOptions.Password,
            VirtualHost = rabbitMqOptions.VirtualHost
        };

        _connection = factory.CreateConnection();

        using var bootstrapChannel = _connection.CreateModel();
        bootstrapChannel.ExchangeDeclare(rabbitMqOptions.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false);
    }

    public async Task PublishAsync<TEvent>(
        MessageEnvelope<TEvent> envelope,
        string routingKey,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        await _publishLock.WaitAsync(cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var channel = _connection.CreateModel();
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, _serializerOptions));
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";
            properties.MessageId = envelope.MessageId.ToString();
            properties.CorrelationId = envelope.CorrelationId.ToString();
            properties.Type = envelope.EventType;
            properties.Timestamp = new AmqpTimestamp(envelope.OccurredAt.ToUnixTimeSeconds());
            properties.Headers = new Dictionary<string, object>
            {
                ["event-version"] = envelope.EventVersion,
                ["occurred-at"] = envelope.OccurredAt.ToString("O")
            };

            if (envelope.SagaId.HasValue)
            {
                properties.Headers["saga-id"] = envelope.SagaId.Value.ToString();
            }

            channel.BasicPublish(
                exchange: _exchangeName,
                routingKey: routingKey,
                basicProperties: properties,
                body: body);

            _logger.LogInformation(
                "Published event {EventType} with MessageId {MessageId} to routing key {RoutingKey}.",
                envelope.EventType,
                envelope.MessageId,
                routingKey);

            return;
        }
        finally
        {
            _publishLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _publishLock.Dispose();
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }
}

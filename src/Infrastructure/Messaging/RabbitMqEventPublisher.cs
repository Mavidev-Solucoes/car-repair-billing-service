using System.Text;
using System.Text.Json;
using Application.Abstractions.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Infrastructure.Messaging;

public sealed class RabbitMqEventPublisher : IEventPublisher
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    public RabbitMqEventPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqEventPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task PublishAsync<TEvent>(
        MessageEnvelope<TEvent> envelope,
        string routingKey,
        CancellationToken cancellationToken = default)
        where TEvent : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost
        };

        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        channel.ExchangeDeclare(_options.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false);

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
            exchange: _options.ExchangeName,
            routingKey: routingKey,
            basicProperties: properties,
            body: body);

        _logger.LogInformation(
            "Published event {EventType} with MessageId {MessageId} to routing key {RoutingKey}.",
            envelope.EventType,
            envelope.MessageId,
            routingKey);

        return Task.CompletedTask;
    }
}

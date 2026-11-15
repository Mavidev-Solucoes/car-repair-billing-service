using System.Text;
using System.Text.Json;
using Application.Abstractions.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Infrastructure.Messaging;

public sealed class RabbitMqEventPublisher : IEventPublisher, IDisposable, IAsyncDisposable
{
    private readonly string _exchangeName;
    private readonly ILogger<RabbitMqEventPublisher> _logger;
    private readonly IConnection _connection;
    private readonly SemaphoreSlim _publishLock = new(1, 1);

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

    public async Task PublishAsync(IntegrationMessage message, CancellationToken cancellationToken = default)
    {
        await _publishLock.WaitAsync(cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var channel = _connection.CreateModel();
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, IntegrationMessage.SerializerOptions));
            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";
            properties.MessageId = message.MessageId.ToString();
            properties.CorrelationId = message.CorrelationId;
            properties.Type = message.Type;
            properties.Timestamp = new AmqpTimestamp(new DateTimeOffset(DateTime.SpecifyKind(message.OccurredAtUtc, DateTimeKind.Utc)).ToUnixTimeSeconds());
            properties.Headers ??= new Dictionary<string, object>();
            MessageTracing.Inject(properties.Headers);

            channel.BasicPublish(
                exchange: _exchangeName,
                routingKey: message.Type,
                basicProperties: properties,
                body: body);

            _logger.LogInformation(
                "Published event {EventType} with MessageId {MessageId} to routing key {RoutingKey}.",
                message.Type,
                message.MessageId,
                message.Type);

            return;
        }
        finally
        {
            _publishLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _publishLock.Dispose();
        _connection.Dispose();
    }
}

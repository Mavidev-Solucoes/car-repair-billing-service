using System.Text.Json;
using Application.Abstractions.Messaging;
using RabbitMQ.Client;

namespace IntegrationTests;

/// <summary>Fila temporária ligada a car-repair.events para conferir o que o billing publicou.</summary>
public sealed class EventsProbe : IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly string _queue;
    private readonly List<IntegrationMessage> _received = [];

    public EventsProbe(BillingFixture fixture)
    {
        _connection = fixture.CreateRawConnection();
        _channel = _connection.CreateModel();
        _channel.ExchangeDeclare("car-repair.events", ExchangeType.Topic, durable: true, autoDelete: false);
        _queue = _channel.QueueDeclare().QueueName;
        _channel.QueueBind(_queue, "car-repair.events", "#");
    }

    public async Task<IntegrationMessage> WaitForAsync(string type, string correlationId)
    {
        return await BillingFixture.WaitForAsync(() =>
        {
            while (_channel.BasicGet(_queue, true) is { } delivery)
            {
                _received.Add(JsonSerializer.Deserialize<IntegrationMessage>(delivery.Body.Span, IntegrationMessage.SerializerOptions)!);
            }

            return Task.FromResult(_received.FirstOrDefault(message => message.Type == type && message.CorrelationId == correlationId));
        });
    }

    public void Dispose()
    {
        _channel.Dispose();
        _connection.Dispose();
    }
}

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Infrastructure.Messaging;

public sealed class RabbitMqCommandConsumer : ICommandConsumer, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqCommandConsumer> _logger;
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly SemaphoreSlim _channelLock = new(1, 1);
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, byte> _subscriptions = new();

    public RabbitMqCommandConsumer(IOptions<RabbitMqOptions> options, ILogger<RabbitMqCommandConsumer> logger)
    {
        _options = options.Value;
        _logger = logger;

        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.ExchangeDeclare(_options.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false);
        _channel.ExchangeDeclare(_options.DeadLetterExchangeName, ExchangeType.Topic, durable: true, autoDelete: false);
        _channel.BasicQos(prefetchSize: 0, prefetchCount: _options.PrefetchCount, global: false);
    }

    public async Task SubscribeAsync<TCommand>(
        string queueName,
        string routingKey,
        Func<MessageEnvelope<TCommand>, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
        where TCommand : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_subscriptions.TryAdd(queueName, 0))
        {
            return;
        }

        var deadLetterQueueName = $"{queueName}{_options.DeadLetterQueueSuffix}";
        await ExecuteChannelActionAsync(() =>
        {
            _channel.QueueDeclare(deadLetterQueueName, durable: true, exclusive: false, autoDelete: false);
            _channel.QueueBind(deadLetterQueueName, _options.DeadLetterExchangeName, routingKey);

            _channel.QueueDeclare(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object>
                {
                    ["x-dead-letter-exchange"] = _options.DeadLetterExchangeName,
                    ["x-dead-letter-routing-key"] = routingKey
                });

            _channel.QueueBind(queueName, _options.ExchangeName, routingKey);
        });

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (_, eventArgs) =>
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<MessageEnvelope<TCommand>>(
                    eventArgs.Body.Span,
                    _serializerOptions)
                    ?? throw new InvalidOperationException("Message envelope payload is invalid.");

                await handler(envelope, cancellationToken);
                await ExecuteChannelActionAsync(() =>
                {
                    _channel.BasicAck(eventArgs.DeliveryTag, multiple: false);
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await ExecuteChannelActionAsync(() =>
                {
                    _channel.BasicNack(eventArgs.DeliveryTag, multiple: false, requeue: true);
                });
            }
            catch (Exception exception)
            {
                var retryCount = GetRetryCount(eventArgs.BasicProperties.Headers);

                if (retryCount < _options.RetryCount)
                {
                    await RetryAsync(eventArgs, retryCount + 1, cancellationToken);
                    await ExecuteChannelActionAsync(() =>
                    {
                        _channel.BasicAck(eventArgs.DeliveryTag, multiple: false);
                    });

                    _logger.LogWarning(
                        exception,
                        "Retrying command {RoutingKey}. Attempt {Attempt}/{MaxAttempts}.",
                        routingKey,
                        retryCount + 1,
                        _options.RetryCount);
                }
                else
                {
                    await ExecuteChannelActionAsync(() =>
                    {
                        _channel.BasicNack(eventArgs.DeliveryTag, multiple: false, requeue: false);
                    });

                    _logger.LogError(
                        exception,
                        "Command {RoutingKey} moved to dead-letter queue after {Retries} retries.",
                        routingKey,
                        retryCount);
                }
            }
        };

        await ExecuteChannelActionAsync(() =>
        {
            _channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
        });

        _logger.LogInformation("Subscribed command consumer to queue {QueueName} with routing key {RoutingKey}.", queueName, routingKey);

        return;
    }

    private async Task RetryAsync(BasicDeliverEventArgs eventArgs, int retryCount, CancellationToken cancellationToken)
    {
        if (_options.RetryDelayMilliseconds > 0)
        {
            await Task.Delay(_options.RetryDelayMilliseconds, cancellationToken);
        }

        await ExecuteChannelActionAsync(() =>
        {
            var retryProperties = _channel.CreateBasicProperties();
            retryProperties.Persistent = true;
            retryProperties.ContentType = eventArgs.BasicProperties.ContentType;
            retryProperties.CorrelationId = eventArgs.BasicProperties.CorrelationId;
            retryProperties.MessageId = eventArgs.BasicProperties.MessageId;
            retryProperties.Type = eventArgs.BasicProperties.Type;
            retryProperties.Timestamp = eventArgs.BasicProperties.Timestamp;
            retryProperties.Headers = CloneHeaders(eventArgs.BasicProperties.Headers);
            retryProperties.Headers["x-retry-count"] = retryCount;

            _channel.BasicPublish(
                exchange: _options.ExchangeName,
                routingKey: eventArgs.RoutingKey,
                basicProperties: retryProperties,
                body: eventArgs.Body);
        });
    }

    private async Task ExecuteChannelActionAsync(Action action)
    {
        await _channelLock.WaitAsync();
        try
        {
            action();
        }
        finally
        {
            _channelLock.Release();
        }
    }

    private static IDictionary<string, object> CloneHeaders(IDictionary<string, object>? source)
    {
        var headers = new Dictionary<string, object>();

        if (source is null)
        {
            return headers;
        }

        foreach (var item in source)
        {
            headers[item.Key] = item.Value;
        }

        return headers;
    }

    private static int GetRetryCount(IDictionary<string, object>? headers)
    {
        if (headers is null || !headers.TryGetValue("x-retry-count", out var value))
        {
            return 0;
        }

        return value switch
        {
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            int number => number,
            long number => (int)number,
            _ => 0
        };
    }

    public ValueTask DisposeAsync()
    {
        _channelLock.Dispose();
        _channel.Dispose();
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }
}

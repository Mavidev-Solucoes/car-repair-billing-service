namespace Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ExchangeName { get; set; } = "car-repair.events";
    public string RetryExchangeName { get; set; } = "car-repair.events.retry";
    public string RetryQueueSuffix { get; set; } = ".retry";
    public string DeadLetterExchangeName { get; set; } = "car-repair.events.dlx";
    public string DeadLetterQueueSuffix { get; set; } = ".dlq";
    public int RetryCount { get; set; } = 3;
    public int RetryDelayMilliseconds { get; set; } = 1000;
    public ushort PrefetchCount { get; set; } = 1;
}

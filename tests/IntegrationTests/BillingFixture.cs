using Infrastructure.Messaging;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace IntegrationTests;

/// <summary>
/// Sobe Postgres e RabbitMQ reais (Testcontainers) e a API apontando para eles, com as migrations aplicadas.
/// </summary>
public sealed class BillingFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public string RabbitHost => _rabbit.Hostname;

    public int RabbitPort => _rabbit.GetMappedPublicPort(5672);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Database", _postgres.GetConnectionString());
            builder.UseSetting("RabbitMq:HostName", RabbitHost);
            builder.UseSetting("RabbitMq:Port", RabbitPort.ToString());
            builder.UseSetting("RabbitMq:RetryCount", "1");
            builder.UseSetting("RabbitMq:RetryDelayMilliseconds", "200");
        });

        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
    }

    public RabbitMqOptions RabbitOptions(int retryCount = 3, int retryDelayMilliseconds = 100) => new()
    {
        HostName = RabbitHost,
        Port = RabbitPort,
        RetryCount = retryCount,
        RetryDelayMilliseconds = retryDelayMilliseconds
    };

    public RabbitMqCommandConsumer CreateConsumer(RabbitMqOptions options)
        => new(Options.Create(options), NullLogger<RabbitMqCommandConsumer>.Instance);

    public IConnection CreateRawConnection()
        => new ConnectionFactory { HostName = RabbitHost, Port = RabbitPort, UserName = "guest", Password = "guest" }.CreateConnection();

    public static async Task<T> WaitForAsync<T>(Func<Task<T?>> probe, TimeSpan? timeout = null)
        where T : class
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            var value = await probe();
            if (value is not null)
            {
                return value;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Condição não foi atingida a tempo.");
    }
}

[CollectionDefinition(Name)]
public sealed class BillingCollection : ICollectionFixture<BillingFixture>
{
    public const string Name = "billing";
}

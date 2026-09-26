using Application.Abstractions.Payments;
using Application.Abstractions.Persistence;
using Application.Abstractions.Messaging;
using Infrastructure.Messaging;
using Infrastructure.Payments;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' was not found.");

        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IUnitOfWork>(serviceProvider => serviceProvider.GetRequiredService<ApplicationDbContext>());
        services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
        services.Configure<RabbitMqOptions>(options =>
        {
            var section = configuration.GetSection(RabbitMqOptions.SectionName);

            options.HostName = section["HostName"] ?? options.HostName;
            options.Port = int.TryParse(section["Port"], out var port) ? port : options.Port;
            options.UserName = section["UserName"] ?? options.UserName;
            options.Password = section["Password"] ?? options.Password;
            options.VirtualHost = section["VirtualHost"] ?? options.VirtualHost;
            options.ExchangeName = section["ExchangeName"] ?? options.ExchangeName;
            options.DeadLetterExchangeName = section["DeadLetterExchangeName"] ?? options.DeadLetterExchangeName;
            options.DeadLetterQueueSuffix = section["DeadLetterQueueSuffix"] ?? options.DeadLetterQueueSuffix;
            options.RetryCount = int.TryParse(section["RetryCount"], out var retryCount) ? retryCount : options.RetryCount;
            options.RetryDelayMilliseconds = int.TryParse(section["RetryDelayMilliseconds"], out var retryDelayMilliseconds)
                ? retryDelayMilliseconds
                : options.RetryDelayMilliseconds;
            options.PrefetchCount = ushort.TryParse(section["PrefetchCount"], out var prefetchCount)
                ? prefetchCount
                : options.PrefetchCount;
        });
        services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
        services.AddSingleton<ICommandConsumer, RabbitMqCommandConsumer>();

        return services;
    }
}

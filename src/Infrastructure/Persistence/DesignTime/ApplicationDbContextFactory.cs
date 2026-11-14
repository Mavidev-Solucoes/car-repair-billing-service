using System.Diagnostics.CodeAnalysis;
using Application.Abstractions.Messaging;
using Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Persistence.DesignTime;

/// <summary>
/// Usado só pelo dotnet-ef para gerar migrations, sem subir RabbitMQ nem o resto da aplicação.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("BILLING_DESIGN_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=car_repair_billing;Username=postgres";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ApplicationDbContext(options, new NoOpDomainEventDispatcher());
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

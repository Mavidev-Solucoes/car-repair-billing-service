using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions.Persistence;
using Application.Abstractions.Messaging;
using System.Data;
using Domain.Common;

namespace Infrastructure.Persistence;

public sealed class ApplicationDbContext : DbContext, IUnitOfWork
{
    private readonly IDomainEventDispatcher _domainEventDispatcher;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        IDomainEventDispatcher domainEventDispatcher)
        : base(options)
    {
        _domainEventDispatcher = domainEventDispatcher;
    }

    public DbSet<Budget> Budgets => Set<Budget>();

    public DbSet<BudgetItem> BudgetItems => Set<BudgetItem>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var domainEntities = ChangeTracker.Entries<Entity>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToList();

        var domainEvents = domainEntities
            .SelectMany(entity => entity.DomainEvents)
            .ToList();

        var executionStrategy = Database.CreateExecutionStrategy();
        var changes = await executionStrategy.ExecuteAsync(
            async () => await base.SaveChangesAsync(cancellationToken));

        if (domainEvents.Count == 0)
        {
            return changes;
        }

        await _domainEventDispatcher.DispatchAsync(domainEvents, cancellationToken);

        foreach (var domainEntity in domainEntities)
        {
            domainEntity.ClearDomainEvents();
        }

        return changes;
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(isolationLevel, cancellationToken);
        await action(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions.Persistence;
using System.Data;

namespace Infrastructure.Persistence;

public sealed class ApplicationDbContext : DbContext, IUnitOfWork
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
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

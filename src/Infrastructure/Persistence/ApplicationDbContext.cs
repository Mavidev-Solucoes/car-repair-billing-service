using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions.Persistence;

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
}

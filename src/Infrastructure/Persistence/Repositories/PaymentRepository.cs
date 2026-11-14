using Application.Abstractions.Persistence;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class PaymentRepository : IPaymentRepository
{
    private readonly ApplicationDbContext _context;

    public PaymentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(Payment payment, CancellationToken cancellationToken)
    {
        await _context.Payments.AddAsync(payment, cancellationToken);
    }

    public Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return _context.Payments
            .Include(x => x.Transactions)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Payment>> ListAsync(CancellationToken cancellationToken)
    {
        return await _context.Payments
            .Include(x => x.Transactions)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Payment>> ListByBudgetIdAsync(Guid budgetId, CancellationToken cancellationToken)
    {
        return await _context.Payments
            .Include(x => x.Transactions)
            .Where(x => x.BudgetId == budgetId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public Task<decimal> GetReservedAmountByBudgetIdAsync(Guid budgetId, CancellationToken cancellationToken)
    {
        return _context.Payments
            .Where(x => x.BudgetId == budgetId && x.Status != PaymentStatus.Rejected && x.Status != PaymentStatus.Refunded)
            .SumAsync(x => x.Amount, cancellationToken);
    }
}

using Application.Abstractions.Persistence;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class BudgetRepository : IBudgetRepository
{
    private readonly ApplicationDbContext _context;

    public BudgetRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(Budget budget, CancellationToken cancellationToken)
    {
        await _context.Budgets.AddAsync(budget, cancellationToken);
    }

    public Task<Budget?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return _context.Budgets
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Budget>> ListAsync(CancellationToken cancellationToken)
    {
        return await _context.Budgets
            .Include(x => x.Items)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }
}

using Domain.Entities;

namespace Application.Abstractions.Persistence;

public interface IBudgetRepository
{
    Task AddAsync(Budget budget, CancellationToken cancellationToken);

    Task<Budget?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<Budget>> ListAsync(CancellationToken cancellationToken);
}

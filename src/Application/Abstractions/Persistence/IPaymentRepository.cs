using Domain.Entities;

namespace Application.Abstractions.Persistence;

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken cancellationToken);

    Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}

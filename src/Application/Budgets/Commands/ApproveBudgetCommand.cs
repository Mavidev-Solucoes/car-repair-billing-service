using Application.Abstractions.Persistence;
using MediatR;

namespace Application.Budgets.Commands;

public sealed record ApproveBudgetCommand(Guid BudgetId) : IRequest;

public sealed class ApproveBudgetCommandHandler : IRequestHandler<ApproveBudgetCommand>
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ApproveBudgetCommandHandler(IBudgetRepository budgetRepository, IUnitOfWork unitOfWork)
    {
        _budgetRepository = budgetRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(ApproveBudgetCommand request, CancellationToken cancellationToken)
    {
        var budget = await _budgetRepository.GetByIdAsync(request.BudgetId, cancellationToken)
            ?? throw new KeyNotFoundException($"Budget '{request.BudgetId}' was not found.");

        budget.Approve();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

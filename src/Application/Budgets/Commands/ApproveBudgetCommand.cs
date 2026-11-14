using Application.Abstractions.Persistence;
using FluentValidation;
using MediatR;

namespace Application.Budgets.Commands;

/// <param name="SimulatePaymentFailure">Vai no evento BudgetApproved e faz o gateway recusar o pagamento (demonstração da compensação).</param>
public sealed record ApproveBudgetCommand(Guid BudgetId, bool SimulatePaymentFailure = false) : IRequest;

public sealed class ApproveBudgetCommandValidator : AbstractValidator<ApproveBudgetCommand>
{
    public ApproveBudgetCommandValidator()
    {
        RuleFor(x => x.BudgetId).NotEmpty();
    }
}

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

        budget.Approve(request.SimulatePaymentFailure);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

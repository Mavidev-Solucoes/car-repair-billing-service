using Application.Abstractions.Persistence;
using FluentValidation;
using MediatR;

namespace Application.Budgets.Commands;

public sealed record RejectBudgetCommand(Guid BudgetId, string Reason) : IRequest;

public sealed class RejectBudgetCommandValidator : AbstractValidator<RejectBudgetCommand>
{
    public RejectBudgetCommandValidator()
    {
        RuleFor(x => x.BudgetId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class RejectBudgetCommandHandler : IRequestHandler<RejectBudgetCommand>
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RejectBudgetCommandHandler(IBudgetRepository budgetRepository, IUnitOfWork unitOfWork)
    {
        _budgetRepository = budgetRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(RejectBudgetCommand request, CancellationToken cancellationToken)
    {
        var budget = await _budgetRepository.GetByIdAsync(request.BudgetId, cancellationToken)
            ?? throw new KeyNotFoundException($"Budget '{request.BudgetId}' was not found.");

        budget.Reject(request.Reason);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

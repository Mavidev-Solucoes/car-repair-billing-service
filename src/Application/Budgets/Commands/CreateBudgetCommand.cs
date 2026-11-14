using Application.Abstractions.Persistence;
using FluentValidation;
using MediatR;
using Domain.Entities;

namespace Application.Budgets.Commands;

public sealed record CreateBudgetCommand(
    Guid ServiceOrderId,
    string CustomerName,
    IReadOnlyCollection<CreateBudgetItemModel> Items) : IRequest<Guid>;

public sealed record CreateBudgetItemModel(string Description, decimal UnitPrice, int Quantity);

public sealed class CreateBudgetCommandValidator : AbstractValidator<CreateBudgetCommand>
{
    public CreateBudgetCommandValidator()
    {
        RuleFor(x => x.ServiceOrderId).NotEmpty();
        RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).SetValidator(new CreateBudgetItemModelValidator());
    }
}

public sealed class CreateBudgetItemModelValidator : AbstractValidator<CreateBudgetItemModel>
{
    public CreateBudgetItemModelValidator()
    {
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.UnitPrice).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class CreateBudgetCommandHandler : IRequestHandler<CreateBudgetCommand, Guid>
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateBudgetCommandHandler(IBudgetRepository budgetRepository, IUnitOfWork unitOfWork)
    {
        _budgetRepository = budgetRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(CreateBudgetCommand request, CancellationToken cancellationToken)
    {
        // Idempotência: a saga pode reenviar o comando; uma OS tem um orçamento só.
        var existing = await _budgetRepository.GetByServiceOrderIdAsync(request.ServiceOrderId, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var budget = new Budget(
            request.ServiceOrderId,
            request.CustomerName,
            request.Items.Select(item => new BudgetItem(item.Description, item.UnitPrice, item.Quantity)));

        await _budgetRepository.AddAsync(budget, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return budget.Id;
    }
}

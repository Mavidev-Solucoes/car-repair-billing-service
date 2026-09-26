using Application.Abstractions.Persistence;
using Domain.Enums;
using FluentValidation;
using MediatR;
using Domain.Entities;

namespace Application.Payments.Commands;

public sealed record CreatePaymentCommand(Guid BudgetId, decimal Amount) : IRequest<Guid>;

public sealed class CreatePaymentCommandValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentCommandValidator()
    {
        RuleFor(x => x.BudgetId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

public sealed class CreatePaymentCommandHandler : IRequestHandler<CreatePaymentCommand, Guid>
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreatePaymentCommandHandler(
        IBudgetRepository budgetRepository,
        IPaymentRepository paymentRepository,
        IUnitOfWork unitOfWork)
    {
        _budgetRepository = budgetRepository;
        _paymentRepository = paymentRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(CreatePaymentCommand request, CancellationToken cancellationToken)
    {
        var budget = await _budgetRepository.GetByIdAsync(request.BudgetId, cancellationToken)
            ?? throw new KeyNotFoundException($"Budget '{request.BudgetId}' was not found.");

        if (budget.Status != BudgetStatus.Approved)
        {
            throw new InvalidOperationException("Payment can only be created for approved budgets.");
        }

        if (request.Amount > budget.TotalAmount)
        {
            throw new InvalidOperationException("Payment amount cannot exceed budget total amount.");
        }

        var payment = new Payment(request.BudgetId, request.Amount);
        await _paymentRepository.AddAsync(payment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return payment.Id;
    }
}

using Application.Abstractions.Persistence;
using Domain.Enums;
using FluentValidation;
using MediatR;
using Domain.Entities;
using System.Data;

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
        Guid paymentId = Guid.Empty;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var budget = await _budgetRepository.GetByIdAsync(request.BudgetId, ct)
                ?? throw new KeyNotFoundException($"Budget '{request.BudgetId}' was not found.");

            if (budget.Status != BudgetStatus.Approved)
            {
                throw new InvalidOperationException("Payment can only be created for approved budgets.");
            }

            var reservedAmount = await _paymentRepository.GetReservedAmountByBudgetIdAsync(request.BudgetId, ct);
            var projectedTotal = reservedAmount + request.Amount;

            if (projectedTotal > budget.TotalAmount)
            {
                throw new InvalidOperationException("Payment amount cannot exceed the remaining budget balance.");
            }

            var payment = new Payment(request.BudgetId, request.Amount);
            await _paymentRepository.AddAsync(payment, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            paymentId = payment.Id;
        }, IsolationLevel.Serializable, cancellationToken);

        return paymentId;
    }
}

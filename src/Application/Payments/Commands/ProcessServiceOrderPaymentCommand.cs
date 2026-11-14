using System.Data;
using Application.Abstractions.Payments;
using Application.Abstractions.Persistence;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;
using MediatR;

namespace Application.Payments.Commands;

/// <summary>
/// ProcessPaymentCommand da saga: cobra o saldo do orçamento aprovado da OS no gateway.
/// O resultado sai como PaymentApproved ou PaymentRejected.
/// </summary>
public sealed record ProcessServiceOrderPaymentCommand(
    Guid ServiceOrderId,
    Guid? BudgetId,
    bool SimulateFailure) : IRequest<Guid>;

public sealed class ProcessServiceOrderPaymentCommandValidator : AbstractValidator<ProcessServiceOrderPaymentCommand>
{
    public ProcessServiceOrderPaymentCommandValidator()
    {
        RuleFor(x => x.ServiceOrderId).NotEmpty();
    }
}

public sealed class ProcessServiceOrderPaymentCommandHandler : IRequestHandler<ProcessServiceOrderPaymentCommand, Guid>
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUnitOfWork _unitOfWork;

    public ProcessServiceOrderPaymentCommandHandler(
        IBudgetRepository budgetRepository,
        IPaymentRepository paymentRepository,
        IPaymentGateway paymentGateway,
        IUnitOfWork unitOfWork)
    {
        _budgetRepository = budgetRepository;
        _paymentRepository = paymentRepository;
        _paymentGateway = paymentGateway;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(ProcessServiceOrderPaymentCommand request, CancellationToken cancellationToken)
    {
        var budget = await FindBudgetAsync(request, cancellationToken);

        if (budget.Status != BudgetStatus.Approved)
        {
            throw new InvalidOperationException(
                $"Budget '{budget.Id}' is {budget.Status}; only approved budgets can be charged.");
        }

        var payments = await _paymentRepository.ListByBudgetIdAsync(budget.Id, cancellationToken);

        // Idempotência: comando repetido depois de o pagamento já ter sido decidido não cobra de novo.
        var decided = payments.FirstOrDefault(payment => payment.Status is PaymentStatus.Approved or PaymentStatus.Rejected);
        if (decided is not null)
        {
            return decided.Id;
        }

        var payment = payments.FirstOrDefault(item => item.Status == PaymentStatus.Pending);
        if (payment is null)
        {
            payment = new Payment(budget.Id, budget.ServiceOrderId, budget.TotalAmount);
            await _unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    await _paymentRepository.AddAsync(payment, ct);
                    await _unitOfWork.SaveChangesAsync(ct);
                },
                IsolationLevel.Serializable,
                cancellationToken);
        }

        var result = await _paymentGateway.ProcessAsync(payment.Id, payment.Amount, request.SimulateFailure, cancellationToken);

        payment.AddTransaction(new PaymentTransaction(result.IsSuccess, result.ExternalReference, result.Message));
        if (result.IsSuccess)
        {
            payment.Approve();
        }
        else
        {
            payment.Reject(result.Message);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return payment.Id;
    }

    private async Task<Budget> FindBudgetAsync(ProcessServiceOrderPaymentCommand request, CancellationToken cancellationToken)
    {
        var budget = request.BudgetId is { } budgetId && budgetId != Guid.Empty
            ? await _budgetRepository.GetByIdAsync(budgetId, cancellationToken)
            : await _budgetRepository.GetByServiceOrderIdAsync(request.ServiceOrderId, cancellationToken);

        if (budget is null || budget.ServiceOrderId != request.ServiceOrderId)
        {
            throw new KeyNotFoundException($"Budget for service order '{request.ServiceOrderId}' was not found.");
        }

        return budget;
    }
}

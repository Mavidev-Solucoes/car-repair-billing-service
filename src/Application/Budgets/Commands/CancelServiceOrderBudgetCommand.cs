using Application.Abstractions.Payments;
using Application.Abstractions.Persistence;
using Domain.Entities;
using Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Budgets.Commands;

/// <summary>
/// CancelBudgetCommand da saga (compensação): estorna os pagamentos aprovados, recusa os pendentes
/// e cancela o orçamento da OS. Repetir o comando não tem efeito.
/// </summary>
public sealed record CancelServiceOrderBudgetCommand(Guid ServiceOrderId, Guid? BudgetId) : IRequest;

public sealed class CancelServiceOrderBudgetCommandValidator : AbstractValidator<CancelServiceOrderBudgetCommand>
{
    public CancelServiceOrderBudgetCommandValidator()
    {
        RuleFor(x => x.ServiceOrderId).NotEmpty();
    }
}

public sealed class CancelServiceOrderBudgetCommandHandler : IRequestHandler<CancelServiceOrderBudgetCommand>
{
    public const string CancelledPaymentReason = "Cancelado pela compensação da saga.";

    private readonly IBudgetRepository _budgetRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CancelServiceOrderBudgetCommandHandler> _logger;

    public CancelServiceOrderBudgetCommandHandler(
        IBudgetRepository budgetRepository,
        IPaymentRepository paymentRepository,
        IPaymentGateway paymentGateway,
        IUnitOfWork unitOfWork,
        ILogger<CancelServiceOrderBudgetCommandHandler> logger)
    {
        _budgetRepository = budgetRepository;
        _paymentRepository = paymentRepository;
        _paymentGateway = paymentGateway;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task Handle(CancelServiceOrderBudgetCommand request, CancellationToken cancellationToken)
    {
        var budget = request.BudgetId is { } budgetId && budgetId != Guid.Empty
            ? await _budgetRepository.GetByIdAsync(budgetId, cancellationToken)
            : await _budgetRepository.GetByServiceOrderIdAsync(request.ServiceOrderId, cancellationToken);

        if (budget is null || budget.ServiceOrderId != request.ServiceOrderId)
        {
            _logger.LogWarning("No budget to cancel for service order {ServiceOrderId}.", request.ServiceOrderId);
            return;
        }

        if (budget.Status is BudgetStatus.Cancelled or BudgetStatus.Rejected)
        {
            _logger.LogInformation("Budget {BudgetId} is already {Status}; nothing to compensate.", budget.Id, budget.Status);
            return;
        }

        var refundedPaymentIds = new List<Guid>();
        var payments = await _paymentRepository.ListByBudgetIdAsync(budget.Id, cancellationToken);

        foreach (var payment in payments)
        {
            switch (payment.Status)
            {
                case PaymentStatus.Approved:
                    await RefundAsync(payment, cancellationToken);
                    refundedPaymentIds.Add(payment.Id);
                    break;
                case PaymentStatus.Pending:
                    payment.Reject(CancelledPaymentReason);
                    break;
            }
        }

        budget.Cancel(refundedPaymentIds);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task RefundAsync(Payment payment, CancellationToken cancellationToken)
    {
        var chargeReference = payment.Transactions
            .Where(transaction => transaction.IsSuccess)
            .OrderByDescending(transaction => transaction.CreatedAtUtc)
            .Select(transaction => transaction.ExternalReference)
            .FirstOrDefault() ?? payment.Id.ToString("N");

        var result = await _paymentGateway.RefundAsync(payment.Id, chargeReference, payment.Amount, cancellationToken);
        if (!result.IsSuccess)
        {
            // Erro transitório do gateway: a mensagem volta para retry e o estorno é tentado de novo.
            throw new InvalidOperationException($"Refund of payment '{payment.Id}' failed: {result.Message}");
        }

        payment.Refund(new PaymentTransaction(true, result.ExternalReference, result.Message));
    }
}

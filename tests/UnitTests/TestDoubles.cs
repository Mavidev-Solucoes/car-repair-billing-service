using Application.Abstractions.Messaging;
using Application.Abstractions.Payments;
using Application.Abstractions.Persistence;
using Domain.Entities;
using Domain.Enums;

namespace UnitTests;

internal sealed class InMemoryBudgetRepository(params Budget[] budgets) : IBudgetRepository
{
    public List<Budget> Items { get; } = [.. budgets];

    public Task AddAsync(Budget budget, CancellationToken cancellationToken)
    {
        Items.Add(budget);
        return Task.CompletedTask;
    }

    public Task<Budget?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(Items.FirstOrDefault(budget => budget.Id == id));

    public Task<Budget?> GetByServiceOrderIdAsync(Guid serviceOrderId, CancellationToken cancellationToken)
        => Task.FromResult(Items.FirstOrDefault(budget => budget.ServiceOrderId == serviceOrderId));

    public Task<IReadOnlyCollection<Budget>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyCollection<Budget>>(Items);
}

internal sealed class InMemoryPaymentRepository(params Payment[] payments) : IPaymentRepository
{
    public List<Payment> Items { get; } = [.. payments];

    public Task AddAsync(Payment payment, CancellationToken cancellationToken)
    {
        Items.Add(payment);
        return Task.CompletedTask;
    }

    public Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(Items.FirstOrDefault(payment => payment.Id == id));

    public Task<IReadOnlyCollection<Payment>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyCollection<Payment>>(Items);

    public Task<IReadOnlyCollection<Payment>> ListByBudgetIdAsync(Guid budgetId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyCollection<Payment>>(Items.Where(payment => payment.BudgetId == budgetId).ToList());

    public Task<decimal> GetReservedAmountByBudgetIdAsync(Guid budgetId, CancellationToken cancellationToken)
        => Task.FromResult(Items
            .Where(x => x.BudgetId == budgetId && x.Status is not (PaymentStatus.Rejected or PaymentStatus.Refunded))
            .Sum(x => x.Amount));
}

internal sealed class NoOpUnitOfWork : IUnitOfWork
{
    public int SaveChangesCalls { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCalls++;
        return Task.FromResult(1);
    }

    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        System.Data.IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
        => action(cancellationToken);
}

internal sealed class StubPaymentGateway(PaymentGatewayResult result, PaymentGatewayResult? refundResult = null) : IPaymentGateway
{
    public int ProcessCalls { get; private set; }

    public List<string> RefundedReferences { get; } = [];

    public Task<PaymentGatewayResult> ProcessAsync(Guid paymentId, decimal amount, bool simulateFailure, CancellationToken cancellationToken)
    {
        ProcessCalls++;
        return Task.FromResult(result);
    }

    public Task<PaymentGatewayResult> RefundAsync(Guid paymentId, string externalReference, decimal amount, CancellationToken cancellationToken)
    {
        RefundedReferences.Add(externalReference);
        return Task.FromResult(refundResult ?? PaymentGatewayResult.Approved("refund-ref", "refunded"));
    }

    /// <summary>O que o gateway responde nas consultas (webhook e sincronização).</summary>
    public GatewayPaymentStatus? GatewayStatus { get; set; }

    public List<string> Lookups { get; } = [];

    public Task<GatewayPaymentStatus?> FindAsync(string gatewayPaymentId, CancellationToken cancellationToken)
    {
        Lookups.Add(gatewayPaymentId);
        return Task.FromResult(GatewayStatus);
    }

    public Task<GatewayPaymentStatus?> FindByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        Lookups.Add(paymentId.ToString("D"));
        return Task.FromResult(GatewayStatus);
    }
}

internal sealed class RecordingEventPublisher : IEventPublisher
{
    public List<IntegrationMessage> Messages { get; } = [];

    public Task PublishAsync(IntegrationMessage message, CancellationToken cancellationToken = default)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }
}

internal static class Builders
{
    public static Budget ApprovedBudget(Guid serviceOrderId, decimal price = 150m)
    {
        var budget = new Budget(serviceOrderId, "Maria", [new BudgetItem("Troca de óleo", price, 1)]);
        budget.Approve();
        return budget;
    }

    public static Payment ApprovedPayment(Budget budget)
    {
        var payment = new Payment(budget.Id, budget.ServiceOrderId, budget.TotalAmount);
        payment.AddTransaction(new PaymentTransaction(true, "charge-ref", "approved"));
        payment.Approve();
        return payment;
    }
}

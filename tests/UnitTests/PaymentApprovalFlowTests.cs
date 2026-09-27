using Application.Abstractions.Messaging;
using Application.Abstractions.Payments;
using Application.Abstractions.Persistence;
using Application.Payments.Commands;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using Domain.Events;
using Infrastructure.Messaging;

namespace UnitTests;

public sealed class PaymentApprovalFlowTests
{
    [Fact]
    public async Task ApprovePayment_ShouldApproveAndStoreSuccessfulTransaction()
    {
        var payment = new Payment(Guid.NewGuid(), 120m);
        var repository = new InMemoryPaymentRepository(payment);
        var gateway = new StubPaymentGateway(new PaymentGatewayResult(true, "gateway-ref", "approved"));
        var unitOfWork = new NoOpUnitOfWork();
        var handler = new ApprovePaymentCommandHandler(repository, gateway, unitOfWork);

        await handler.Handle(new ApprovePaymentCommand(payment.Id, false), CancellationToken.None);

        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.NotNull(payment.ApprovedAtUtc);
        var transaction = Assert.Single(payment.Transactions);
        Assert.True(transaction.IsSuccess);
        Assert.Equal("gateway-ref", transaction.ExternalReference);
        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task ApprovePayment_ShouldRejectAndStoreFailedTransaction_WhenGatewayFails()
    {
        var payment = new Payment(Guid.NewGuid(), 120m);
        var repository = new InMemoryPaymentRepository(payment);
        var gateway = new StubPaymentGateway(new PaymentGatewayResult(false, "gateway-ref", "gateway failure"));
        var unitOfWork = new NoOpUnitOfWork();
        var handler = new ApprovePaymentCommandHandler(repository, gateway, unitOfWork);

        await handler.Handle(new ApprovePaymentCommand(payment.Id, true), CancellationToken.None);

        Assert.Equal(PaymentStatus.Rejected, payment.Status);
        Assert.Equal("gateway failure", payment.RejectionReason);
        var transaction = Assert.Single(payment.Transactions);
        Assert.False(transaction.IsSuccess);
        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task CreatePayment_ShouldThrow_WhenRequestedAmountExceedsRemainingBudgetBalance()
    {
        var budget = new Budget("Customer", [new BudgetItem("Part", 150m, 1)]);
        budget.Approve();

        var budgetRepository = new InMemoryBudgetRepository(budget);
        var paymentRepository = new InMemoryPaymentRepository(new Payment(budget.Id, 100m));
        var unitOfWork = new NoOpUnitOfWork();
        var handler = new CreatePaymentCommandHandler(budgetRepository, paymentRepository, unitOfWork);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new CreatePaymentCommand(budget.Id, 60m), CancellationToken.None));

        Assert.Equal("Payment amount cannot exceed the remaining budget balance.", exception.Message);
    }

    [Fact]
    public async Task DomainEventDispatcher_ShouldPublishKnownDomainEventsWithExpectedRoutingKeys()
    {
        var publisher = new RecordingEventPublisher();
        var dispatcher = new DomainEventDispatcher(publisher);
        var events = new IDomainEvent[]
        {
            new BudgetCreatedDomainEvent(Guid.NewGuid()),
            new BudgetApprovedDomainEvent(Guid.NewGuid()),
            new BudgetRejectedDomainEvent(Guid.NewGuid(), "reason"),
            new PaymentCreatedDomainEvent(Guid.NewGuid(), Guid.NewGuid()),
            new PaymentApprovedDomainEvent(Guid.NewGuid()),
            new PaymentRejectedDomainEvent(Guid.NewGuid(), "reason")
        };

        await dispatcher.DispatchAsync(events, CancellationToken.None);

        Assert.Equal(
            ["budgets.created", "budgets.approved", "budgets.rejected", "payments.created", "payments.approved", "payments.rejected"],
            publisher.RoutingKeys);
    }

    private sealed class InMemoryBudgetRepository : IBudgetRepository
    {
        private readonly Budget _budget;

        public InMemoryBudgetRepository(Budget budget)
        {
            _budget = budget;
        }

        public Task AddAsync(Budget budget, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Budget?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(id == _budget.Id ? _budget : null);

        public Task<IReadOnlyCollection<Budget>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<Budget>>([_budget]);
    }

    private sealed class InMemoryPaymentRepository : IPaymentRepository
    {
        private readonly Dictionary<Guid, Payment> _payments;

        public InMemoryPaymentRepository(params Payment[] payments)
        {
            _payments = payments.ToDictionary(x => x.Id);
        }

        public Task AddAsync(Payment payment, CancellationToken cancellationToken)
        {
            _payments[payment.Id] = payment;
            return Task.CompletedTask;
        }

        public Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(_payments.TryGetValue(id, out var payment) ? payment : null);

        public Task<IReadOnlyCollection<Payment>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<Payment>>(_payments.Values.ToList());

        public Task<decimal> GetReservedAmountByBudgetIdAsync(Guid budgetId, CancellationToken cancellationToken)
            => Task.FromResult(_payments.Values.Where(x => x.BudgetId == budgetId && x.Status != PaymentStatus.Rejected).Sum(x => x.Amount));
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
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

    private sealed class StubPaymentGateway : IPaymentGateway
    {
        private readonly PaymentGatewayResult _result;

        public StubPaymentGateway(PaymentGatewayResult result)
        {
            _result = result;
        }

        public Task<PaymentGatewayResult> ProcessAsync(
            Guid paymentId,
            decimal amount,
            bool simulateFailure,
            CancellationToken cancellationToken)
            => Task.FromResult(_result);
    }

    private sealed class RecordingEventPublisher : IEventPublisher
    {
        public List<string> RoutingKeys { get; } = [];

        public Task PublishAsync<TEvent>(MessageEnvelope<TEvent> envelope, string routingKey, CancellationToken cancellationToken = default)
            where TEvent : class
        {
            RoutingKeys.Add(routingKey);
            return Task.CompletedTask;
        }
    }
}

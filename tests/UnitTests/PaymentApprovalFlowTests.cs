using Application.Abstractions.Payments;
using Application.Payments.Commands;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using Domain.Events;
using Infrastructure.Messaging;

namespace UnitTests;

public sealed class PaymentApprovalFlowTests
{
    private static readonly Guid ServiceOrderId = Guid.NewGuid();

    [Fact]
    public async Task ApprovePayment_ShouldApproveAndStoreSuccessfulTransaction()
    {
        var payment = new Payment(Guid.NewGuid(), ServiceOrderId, 120m);
        var repository = new InMemoryPaymentRepository(payment);
        var gateway = new StubPaymentGateway(PaymentGatewayResult.Approved("gateway-ref", "approved"));
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
        var payment = new Payment(Guid.NewGuid(), ServiceOrderId, 120m);
        var repository = new InMemoryPaymentRepository(payment);
        var gateway = new StubPaymentGateway(PaymentGatewayResult.Rejected("gateway-ref", "gateway failure"));
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
        var budget = Builders.ApprovedBudget(ServiceOrderId);

        var budgetRepository = new InMemoryBudgetRepository(budget);
        var paymentRepository = new InMemoryPaymentRepository(new Payment(budget.Id, ServiceOrderId, 100m));
        var unitOfWork = new NoOpUnitOfWork();
        var handler = new CreatePaymentCommandHandler(budgetRepository, paymentRepository, unitOfWork);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new CreatePaymentCommand(budget.Id, 60m), CancellationToken.None));

        Assert.Equal("Payment amount cannot exceed the remaining budget balance.", exception.Message);
    }

    [Fact]
    public async Task CreatePayment_ShouldCopyServiceOrderFromBudget()
    {
        var budget = Builders.ApprovedBudget(ServiceOrderId);
        var paymentRepository = new InMemoryPaymentRepository();
        var handler = new CreatePaymentCommandHandler(new InMemoryBudgetRepository(budget), paymentRepository, new NoOpUnitOfWork());

        await handler.Handle(new CreatePaymentCommand(budget.Id, 50m), CancellationToken.None);

        Assert.Equal(ServiceOrderId, Assert.Single(paymentRepository.Items).ServiceOrderId);
    }

    [Fact]
    public async Task DomainEventDispatcher_ShouldPublishEventsFollowingTheContract()
    {
        var publisher = new RecordingEventPublisher();
        var dispatcher = new DomainEventDispatcher(publisher);
        var budgetId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var events = new IDomainEvent[]
        {
            new BudgetCreatedDomainEvent(budgetId, ServiceOrderId, 150m),
            new BudgetApprovedDomainEvent(budgetId, ServiceOrderId, 150m, false),
            new BudgetRejectedDomainEvent(budgetId, ServiceOrderId, "reason"),
            new BudgetCancelledDomainEvent(budgetId, ServiceOrderId, [paymentId]),
            new PaymentCreatedDomainEvent(paymentId, budgetId, ServiceOrderId),
            new PaymentApprovedDomainEvent(paymentId, budgetId, ServiceOrderId, 150m),
            new PaymentRejectedDomainEvent(paymentId, budgetId, ServiceOrderId, "reason"),
            new PaymentRefundedDomainEvent(paymentId, budgetId, ServiceOrderId, 150m)
        };

        await dispatcher.DispatchAsync(events, CancellationToken.None);

        Assert.Equal(
            ["BudgetCreated", "BudgetApproved", "BudgetRejected", "BudgetCancelled", "PaymentCreated", "PaymentApproved", "PaymentRejected", "PaymentRefunded"],
            publisher.Messages.Select(message => message.Type).ToArray());
        Assert.All(publisher.Messages, message => Assert.Equal(ServiceOrderId.ToString("D"), message.CorrelationId));

        var approved = publisher.Messages[5];
        Assert.Equal(paymentId, approved.GetGuid("paymentId"));
        Assert.Equal(budgetId, approved.GetGuid("budgetId"));
        Assert.Equal(150m, approved.Payload!["amount"]!.GetValue<decimal>());
        Assert.False(approved.Payload.ContainsKey("occurredOnUtc"));
    }

    [Fact]
    public async Task DomainEventDispatcher_ShouldThrow_WhenEventIsNotLinkedToServiceOrder()
    {
        var dispatcher = new DomainEventDispatcher(new RecordingEventPublisher());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync([new UnknownDomainEvent()], CancellationToken.None));

        Assert.Contains("not linked to a service order", exception.Message);
    }

    private sealed record UnknownDomainEvent : IDomainEvent
    {
        public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
    }
}

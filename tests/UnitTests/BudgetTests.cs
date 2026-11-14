using Domain.Entities;
using Domain.Enums;
using Domain.Events;

namespace UnitTests;

public sealed class BudgetTests
{
    private static readonly Guid ServiceOrderId = Guid.NewGuid();

    [Fact]
    public void Constructor_ShouldRaiseCreatedEventWithServiceOrderAndTotal()
    {
        var budget = new Budget(ServiceOrderId, "Customer", [new BudgetItem("Part", 100m, 2)]);

        var created = Assert.IsType<BudgetCreatedDomainEvent>(Assert.Single(budget.DomainEvents));
        Assert.Equal(ServiceOrderId, created.ServiceOrderId);
        Assert.Equal(200m, created.TotalAmount);
    }

    [Fact]
    public void Constructor_ShouldRequireServiceOrderId()
    {
        Assert.Throws<ArgumentException>(() => new Budget(Guid.Empty, "Customer", [new BudgetItem("Part", 100m, 1)]));
    }

    [Fact]
    public void Approve_ShouldSetStatusAndApprovedDate()
    {
        var budget = new Budget(ServiceOrderId, "Customer", [new BudgetItem("Part", 100m, 1)]);

        budget.Approve(simulatePaymentFailure: true);

        Assert.Equal(BudgetStatus.Approved, budget.Status);
        Assert.NotNull(budget.ApprovedAtUtc);
        Assert.True(budget.DomainEvents.OfType<BudgetApprovedDomainEvent>().Single().SimulatePaymentFailure);
    }

    [Fact]
    public void Reject_AfterApprove_ShouldThrowInvalidOperationException()
    {
        var budget = new Budget(ServiceOrderId, "Customer", [new BudgetItem("Part", 100m, 1)]);
        budget.Approve();

        Assert.Throws<InvalidOperationException>(() => budget.Reject("not valid"));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenItemsAreEmpty()
    {
        Assert.Throws<ArgumentException>(() => new Budget(ServiceOrderId, "Customer", []));
    }

    [Fact]
    public void Reject_ShouldThrow_WhenReasonIsBlank()
    {
        var budget = new Budget(ServiceOrderId, "Customer", [new BudgetItem("Part", 100m, 1)]);

        Assert.Throws<ArgumentException>(() => budget.Reject("   "));
    }

    [Fact]
    public void Cancel_ShouldWorkFromPendingOrApprovedOnly()
    {
        var pending = new Budget(ServiceOrderId, "Customer", [new BudgetItem("Part", 100m, 1)]);
        pending.Cancel([]);
        Assert.Equal(BudgetStatus.Cancelled, pending.Status);
        Assert.NotNull(pending.CancelledAtUtc);

        var refundedId = Guid.NewGuid();
        var approved = new Budget(ServiceOrderId, "Customer", [new BudgetItem("Part", 100m, 1)]);
        approved.Approve();
        approved.Cancel([refundedId]);
        Assert.Equal([refundedId], approved.DomainEvents.OfType<BudgetCancelledDomainEvent>().Single().RefundedPaymentIds);

        Assert.Throws<InvalidOperationException>(() => approved.Cancel([]));
    }

    [Fact]
    public void Refund_ShouldOnlyApplyToApprovedPayments()
    {
        var payment = new Payment(Guid.NewGuid(), ServiceOrderId, 80m);
        Assert.Throws<InvalidOperationException>(() => payment.Refund(new PaymentTransaction(true, "refund", "ok")));

        payment.AddTransaction(new PaymentTransaction(true, "charge", "ok"));
        payment.Approve();
        payment.Refund(new PaymentTransaction(true, "refund", "ok"));

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.NotNull(payment.RefundedAtUtc);
        Assert.Equal(2, payment.Transactions.Count);
        Assert.Single(payment.DomainEvents.OfType<PaymentRefundedDomainEvent>());
    }

    [Fact]
    public void Payment_ShouldRequireServiceOrderId()
    {
        Assert.Throws<ArgumentException>(() => new Payment(Guid.NewGuid(), Guid.Empty, 10m));
    }
}

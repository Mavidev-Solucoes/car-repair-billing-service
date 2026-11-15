using Domain.Common;
using Domain.Enums;
using Domain.Events;

namespace Domain.Entities;

public sealed class Payment : Entity
{
    private readonly List<PaymentTransaction> _transactions = [];

    private Payment()
    {
    }

    public Payment(Guid budgetId, Guid serviceOrderId, decimal amount)
    {
        if (serviceOrderId == Guid.Empty)
        {
            throw new ArgumentException("Service order identifier is required.", nameof(serviceOrderId));
        }

        if (budgetId == Guid.Empty)
        {
            throw new ArgumentException("Budget identifier is required.", nameof(budgetId));
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        }

        Id = Guid.NewGuid();
        BudgetId = budgetId;
        ServiceOrderId = serviceOrderId;
        Amount = amount;
        Status = PaymentStatus.Pending;
        RaiseDomainEvent(new PaymentCreatedDomainEvent(Id, budgetId, serviceOrderId));
    }

    public Guid Id { get; private set; }

    public Guid BudgetId { get; private set; }

    public Guid ServiceOrderId { get; private set; }

    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public DateTime? ApprovedAtUtc { get; private set; }

    public DateTime? RejectedAtUtc { get; private set; }

    public string? RejectionReason { get; private set; }

    public DateTime? RefundedAtUtc { get; private set; }

    /// <summary>Link de checkout do gateway (Mercado Pago) enquanto o pagamento aguarda o cliente pagar.</summary>
    public string? CheckoutUrl { get; private set; }

    /// <summary>Referência do checkout no gateway (id da preferência no Mercado Pago).</summary>
    public string? CheckoutReference { get; private set; }

    public IReadOnlyCollection<PaymentTransaction> Transactions => _transactions.AsReadOnly();

    public void AddTransaction(PaymentTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException("Transactions can only be added to pending payments.");
        }

        transaction.SetPaymentId(Id);
        _transactions.Add(transaction);
    }

    /// <summary>
    /// O gateway criou o checkout e o pagamento fica pendente até o cliente pagar (confirmação via webhook).
    /// </summary>
    public void AwaitCheckout(string checkoutReference, string checkoutUrl)
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException("Only pending payments can wait for checkout.");
        }

        if (string.IsNullOrWhiteSpace(checkoutReference))
        {
            throw new ArgumentException("Checkout reference is required.", nameof(checkoutReference));
        }

        if (string.IsNullOrWhiteSpace(checkoutUrl))
        {
            throw new ArgumentException("Checkout URL is required.", nameof(checkoutUrl));
        }

        CheckoutReference = checkoutReference.Trim();
        CheckoutUrl = checkoutUrl.Trim();
    }

    public void Approve()
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException("Only pending payments can be approved.");
        }

        Status = PaymentStatus.Approved;
        ApprovedAtUtc = DateTime.UtcNow;
        RaiseDomainEvent(new PaymentApprovedDomainEvent(Id, BudgetId, ServiceOrderId, Amount));
    }

    public void Reject(string reason)
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException("Only pending payments can be rejected.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Rejection reason is required.", nameof(reason));
        }

        Status = PaymentStatus.Rejected;
        RejectionReason = reason.Trim();
        RejectedAtUtc = DateTime.UtcNow;
        RaiseDomainEvent(new PaymentRejectedDomainEvent(Id, BudgetId, ServiceOrderId, RejectionReason));
    }

    /// <summary>
    /// Compensação da saga: estorna um pagamento aprovado.
    /// </summary>
    public void Refund(PaymentTransaction refundTransaction)
    {
        ArgumentNullException.ThrowIfNull(refundTransaction);

        if (Status != PaymentStatus.Approved)
        {
            throw new InvalidOperationException("Only approved payments can be refunded.");
        }

        refundTransaction.SetPaymentId(Id);
        _transactions.Add(refundTransaction);
        Status = PaymentStatus.Refunded;
        RefundedAtUtc = DateTime.UtcNow;
        RaiseDomainEvent(new PaymentRefundedDomainEvent(Id, BudgetId, ServiceOrderId, Amount));
    }
}

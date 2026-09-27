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

    public Payment(Guid budgetId, decimal amount)
    {
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
        Amount = amount;
        Status = PaymentStatus.Pending;
        RaiseDomainEvent(new PaymentCreatedDomainEvent(Id, budgetId));
    }

    public Guid Id { get; private set; }

    public Guid BudgetId { get; private set; }

    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public DateTime? ApprovedAtUtc { get; private set; }

    public DateTime? RejectedAtUtc { get; private set; }

    public string? RejectionReason { get; private set; }

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

    public void Approve()
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException("Only pending payments can be approved.");
        }

        Status = PaymentStatus.Approved;
        ApprovedAtUtc = DateTime.UtcNow;
        RaiseDomainEvent(new PaymentApprovedDomainEvent(Id));
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
        RaiseDomainEvent(new PaymentRejectedDomainEvent(Id, RejectionReason));
    }
}

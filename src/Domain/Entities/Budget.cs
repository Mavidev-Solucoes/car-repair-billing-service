using Domain.Common;
using Domain.Enums;
using Domain.Events;

namespace Domain.Entities;

public sealed class Budget : Entity
{
    private readonly List<BudgetItem> _items = [];

    private Budget()
    {
    }

    public Budget(Guid serviceOrderId, string customerName, IEnumerable<BudgetItem> items)
    {
        if (serviceOrderId == Guid.Empty)
        {
            throw new ArgumentException("Service order identifier is required.", nameof(serviceOrderId));
        }

        if (string.IsNullOrWhiteSpace(customerName))
        {
            throw new ArgumentException("Customer name is required.", nameof(customerName));
        }

        var budgetItems = items?.ToList() ?? throw new ArgumentNullException(nameof(items));

        if (budgetItems.Count == 0)
        {
            throw new ArgumentException("At least one budget item is required.", nameof(items));
        }

        Id = Guid.NewGuid();
        ServiceOrderId = serviceOrderId;
        CustomerName = customerName.Trim();
        Status = BudgetStatus.Pending;
        foreach (var item in budgetItems)
        {
            item.SetBudgetId(Id);
            _items.Add(item);
        }
        RaiseDomainEvent(new BudgetCreatedDomainEvent(Id, ServiceOrderId, TotalAmount));
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Id da ordem de serviço no os-service. É o correlationId das mensagens da saga.
    /// </summary>
    public Guid ServiceOrderId { get; private set; }

    public string CustomerName { get; private set; } = string.Empty;

    public BudgetStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public DateTime? ApprovedAtUtc { get; private set; }

    public DateTime? RejectedAtUtc { get; private set; }

    public string? RejectionReason { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }

    public IReadOnlyCollection<BudgetItem> Items => _items.AsReadOnly();

    public decimal TotalAmount => _items.Sum(x => x.Total);

    public void Approve(bool simulatePaymentFailure = false)
    {
        if (Status != BudgetStatus.Pending)
        {
            throw new InvalidOperationException("Only pending budgets can be approved.");
        }

        Status = BudgetStatus.Approved;
        ApprovedAtUtc = DateTime.UtcNow;
        RaiseDomainEvent(new BudgetApprovedDomainEvent(Id, ServiceOrderId, TotalAmount, simulatePaymentFailure));
    }

    public void Reject(string reason)
    {
        if (Status != BudgetStatus.Pending)
        {
            throw new InvalidOperationException("Only pending budgets can be rejected.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Rejection reason is required.", nameof(reason));
        }

        Status = BudgetStatus.Rejected;
        RejectionReason = reason.Trim();
        RejectedAtUtc = DateTime.UtcNow;
        RaiseDomainEvent(new BudgetRejectedDomainEvent(Id, ServiceOrderId, RejectionReason));
    }

    /// <summary>
    /// Compensação da saga: cancela um orçamento pendente ou aprovado.
    /// </summary>
    public void Cancel(IReadOnlyCollection<Guid> refundedPaymentIds)
    {
        ArgumentNullException.ThrowIfNull(refundedPaymentIds);

        if (Status is not (BudgetStatus.Pending or BudgetStatus.Approved))
        {
            throw new InvalidOperationException("Only pending or approved budgets can be cancelled.");
        }

        Status = BudgetStatus.Cancelled;
        CancelledAtUtc = DateTime.UtcNow;
        RaiseDomainEvent(new BudgetCancelledDomainEvent(Id, ServiceOrderId, refundedPaymentIds));
    }
}

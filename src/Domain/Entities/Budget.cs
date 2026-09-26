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

    public Budget(string customerName, IEnumerable<BudgetItem> items)
    {
        Id = Guid.NewGuid();
        CustomerName = customerName;
        Status = BudgetStatus.Pending;
        _items.AddRange(items);
        RaiseDomainEvent(new BudgetCreatedDomainEvent(Id));
    }

    public Guid Id { get; private set; }

    public string CustomerName { get; private set; } = string.Empty;

    public BudgetStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public DateTime? ApprovedAtUtc { get; private set; }

    public DateTime? RejectedAtUtc { get; private set; }

    public string? RejectionReason { get; private set; }

    public IReadOnlyCollection<BudgetItem> Items => _items.AsReadOnly();

    public decimal TotalAmount => _items.Sum(x => x.Total);

    public void Approve()
    {
        if (Status != BudgetStatus.Pending)
        {
            throw new InvalidOperationException("Only pending budgets can be approved.");
        }

        Status = BudgetStatus.Approved;
        ApprovedAtUtc = DateTime.UtcNow;
        RaiseDomainEvent(new BudgetApprovedDomainEvent(Id));
    }

    public void Reject(string reason)
    {
        if (Status != BudgetStatus.Pending)
        {
            throw new InvalidOperationException("Only pending budgets can be rejected.");
        }

        Status = BudgetStatus.Rejected;
        RejectionReason = reason;
        RejectedAtUtc = DateTime.UtcNow;
        RaiseDomainEvent(new BudgetRejectedDomainEvent(Id, reason));
    }
}

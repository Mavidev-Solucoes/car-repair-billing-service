namespace Domain.Entities;

public sealed class BudgetItem
{
    private BudgetItem()
    {
    }

    public BudgetItem(string description, decimal unitPrice, int quantity)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Description is required.", nameof(description));
        }

        if (unitPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price must be greater than zero.");
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        Id = Guid.NewGuid();
        Description = description.Trim();
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    public Guid Id { get; private set; }

    public Guid BudgetId { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    public decimal Total => UnitPrice * Quantity;

    internal void SetBudgetId(Guid budgetId)
    {
        BudgetId = budgetId;
    }
}

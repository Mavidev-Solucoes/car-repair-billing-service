namespace Domain.Entities;

public sealed class BudgetItem
{
    private BudgetItem()
    {
    }

    public BudgetItem(string description, decimal unitPrice, int quantity)
    {
        Id = Guid.NewGuid();
        Description = description;
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

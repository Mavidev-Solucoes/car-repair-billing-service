namespace Api.Contracts;

public sealed record CreateBudgetRequest(string CustomerName, IReadOnlyCollection<CreateBudgetItemRequest> Items);

public sealed record CreateBudgetItemRequest(string Description, decimal UnitPrice, int Quantity);

public sealed record RejectBudgetRequest(string Reason);

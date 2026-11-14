namespace Api.Contracts;

public sealed record CreateBudgetRequest(Guid ServiceOrderId, string CustomerName, IReadOnlyCollection<CreateBudgetItemRequest> Items);

public sealed record CreateBudgetItemRequest(string Description, decimal UnitPrice, int Quantity);

public sealed record RejectBudgetRequest(string Reason);

/// <param name="SimulatePaymentFailure">Faz o gateway recusar o pagamento que a saga vai pedir (demonstração da compensação).</param>
public sealed record ApproveBudgetRequest(bool SimulatePaymentFailure = false);

namespace Api.Contracts;

public sealed record CreatePaymentRequest(Guid BudgetId, decimal Amount);

public sealed record ApprovePaymentRequest(bool SimulateFailure);

public sealed record RejectPaymentRequest(string Reason);

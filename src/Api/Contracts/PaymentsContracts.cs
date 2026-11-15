namespace Api.Contracts;

public sealed record CreatePaymentRequest(Guid BudgetId, decimal Amount);

public sealed record ApprovePaymentRequest(bool SimulateFailure);

public sealed record RejectPaymentRequest(string Reason);

/// <summary>Corpo da notificação do Mercado Pago: { "type": "payment", "data": { "id": "123" } }.</summary>
public sealed record MercadoPagoNotification(string? Type, string? Action, MercadoPagoNotificationData? Data);

public sealed record MercadoPagoNotificationData(string? Id);

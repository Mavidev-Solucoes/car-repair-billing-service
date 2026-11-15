namespace Application.Abstractions.Payments;

public enum PaymentGatewayStatus
{
    Approved = 1,
    Rejected = 2,

    /// <summary>O gateway aguarda o cliente pagar (checkout criado); a confirmação chega depois.</summary>
    Pending = 3
}

public sealed record PaymentGatewayResult(
    PaymentGatewayStatus Status,
    string ExternalReference,
    string Message,
    string? CheckoutUrl = null)
{
    public bool IsSuccess => Status == PaymentGatewayStatus.Approved;

    public static PaymentGatewayResult Approved(string externalReference, string message) =>
        new(PaymentGatewayStatus.Approved, externalReference, message);

    public static PaymentGatewayResult Rejected(string externalReference, string message) =>
        new(PaymentGatewayStatus.Rejected, externalReference, message);

    public static PaymentGatewayResult Pending(string externalReference, string checkoutUrl, string message) =>
        new(PaymentGatewayStatus.Pending, externalReference, message, checkoutUrl);
}

/// <summary>Situação de um pagamento consultada no gateway (webhook ou sincronização manual).</summary>
/// <param name="GatewayPaymentId">Id do pagamento no gateway (no Mercado Pago, o id do pagamento, não da preferência).</param>
/// <param name="PaymentId">Id do Payment do billing, enviado ao gateway como referência externa.</param>
public sealed record GatewayPaymentStatus(
    string GatewayPaymentId,
    Guid PaymentId,
    PaymentGatewayStatus Status,
    string Message);

namespace Application.Abstractions.Payments;

public interface IPaymentGateway
{
    /// <summary>
    /// Cobra o pagamento. Gateways síncronos devolvem Approved/Rejected na hora; o Mercado Pago devolve Pending
    /// com o link de checkout, e a decisão chega depois por <see cref="FindAsync"/>.
    /// </summary>
    Task<PaymentGatewayResult> ProcessAsync(Guid paymentId, decimal amount, bool simulateFailure, CancellationToken cancellationToken);

    Task<PaymentGatewayResult> RefundAsync(Guid paymentId, string externalReference, decimal amount, CancellationToken cancellationToken);

    /// <summary>Consulta um pagamento pelo id do gateway (o que chega no webhook).</summary>
    Task<GatewayPaymentStatus?> FindAsync(string gatewayPaymentId, CancellationToken cancellationToken);

    /// <summary>Consulta o pagamento mais recente do gateway para um Payment do billing (sincronização manual).</summary>
    Task<GatewayPaymentStatus?> FindByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken);
}

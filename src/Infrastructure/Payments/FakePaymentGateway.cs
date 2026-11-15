using Application.Abstractions.Payments;

namespace Infrastructure.Payments;

/// <summary>
/// Gateway local, síncrono: aprova na hora, ou recusa quando a saga pede simulação de falha.
/// Padrão em desenvolvimento, testes e no e2e do kind (PaymentGateway:Provider = Fake).
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    public Task<PaymentGatewayResult> ProcessAsync(
        Guid paymentId,
        decimal amount,
        bool simulateFailure,
        CancellationToken cancellationToken)
    {
        if (simulateFailure)
        {
            return Task.FromResult(PaymentGatewayResult.Rejected($"fake-failure-{paymentId:N}", "Simulated payment failure."));
        }

        return Task.FromResult(PaymentGatewayResult.Approved(
            $"fake-success-{paymentId:N}",
            FormattableString.Invariant($"Simulated successful payment of {amount:0.00}.")));
    }

    public Task<PaymentGatewayResult> RefundAsync(
        Guid paymentId,
        string externalReference,
        decimal amount,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(PaymentGatewayResult.Approved(
            $"fake-refund-{paymentId:N}",
            FormattableString.Invariant($"Simulated refund of {amount:0.00} for {externalReference}.")));
    }

    // O gateway falso decide na hora: não há pagamento pendente para consultar depois.
    public Task<GatewayPaymentStatus?> FindAsync(string gatewayPaymentId, CancellationToken cancellationToken) =>
        Task.FromResult<GatewayPaymentStatus?>(null);

    public Task<GatewayPaymentStatus?> FindByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken) =>
        Task.FromResult<GatewayPaymentStatus?>(null);
}

using Application.Abstractions.Payments;

namespace Infrastructure.Payments;

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
            return Task.FromResult(new PaymentGatewayResult(
                false,
                $"fake-failure-{paymentId:N}",
                "Simulated payment failure."));
        }

        return Task.FromResult(new PaymentGatewayResult(
            true,
            $"fake-success-{paymentId:N}",
            FormattableString.Invariant($"Simulated successful payment of {amount:0.00}.")));
    }

    public Task<PaymentGatewayResult> RefundAsync(
        Guid paymentId,
        string externalReference,
        decimal amount,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new PaymentGatewayResult(
            true,
            $"fake-refund-{paymentId:N}",
            FormattableString.Invariant($"Simulated refund of {amount:0.00} for {externalReference}.")));
    }
}

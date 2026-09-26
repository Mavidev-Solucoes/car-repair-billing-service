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
            $"Simulated successful payment of {amount:0.00}."));
    }
}

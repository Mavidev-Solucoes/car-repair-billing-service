namespace Application.Abstractions.Payments;

public interface IPaymentGateway
{
    Task<PaymentGatewayResult> ProcessAsync(Guid paymentId, decimal amount, bool simulateFailure, CancellationToken cancellationToken);

    Task<PaymentGatewayResult> RefundAsync(Guid paymentId, string externalReference, decimal amount, CancellationToken cancellationToken);
}

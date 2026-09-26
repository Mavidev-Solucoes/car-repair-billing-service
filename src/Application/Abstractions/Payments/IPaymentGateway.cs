namespace Application.Abstractions.Payments;

public interface IPaymentGateway
{
    Task<PaymentGatewayResult> ProcessAsync(Guid paymentId, decimal amount, bool simulateFailure, CancellationToken cancellationToken);
}

namespace Application.Abstractions.Payments;

public sealed record PaymentGatewayResult(bool IsSuccess, string ExternalReference, string Message);

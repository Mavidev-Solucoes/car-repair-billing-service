namespace Domain.Entities;

public sealed class PaymentTransaction
{
    private PaymentTransaction()
    {
    }

    public PaymentTransaction(bool isSuccess, string externalReference, string message)
    {
        if (string.IsNullOrWhiteSpace(externalReference))
        {
            throw new ArgumentException("External reference is required.", nameof(externalReference));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Message is required.", nameof(message));
        }

        Id = Guid.NewGuid();
        IsSuccess = isSuccess;
        ExternalReference = externalReference.Trim();
        Message = message.Trim();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid PaymentId { get; private set; }

    public bool IsSuccess { get; private set; }

    public string ExternalReference { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    internal void SetPaymentId(Guid paymentId)
    {
        PaymentId = paymentId;
    }
}

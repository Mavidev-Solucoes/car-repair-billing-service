namespace Domain.Entities;

public sealed class PaymentTransaction
{
    private PaymentTransaction()
    {
    }

    public PaymentTransaction(bool isSuccess, string externalReference, string message)
    {
        Id = Guid.NewGuid();
        IsSuccess = isSuccess;
        ExternalReference = externalReference;
        Message = message;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid PaymentId { get; private set; }

    public bool IsSuccess { get; private set; }

    public string ExternalReference { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }
}

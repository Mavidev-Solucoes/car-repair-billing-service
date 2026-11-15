using Application.Abstractions.Payments;
using Domain.Entities;
using Domain.Enums;

namespace Application.Payments;

/// <summary>Aplica no Payment o que o gateway respondeu: aprova, recusa ou deixa aguardando o checkout.</summary>
public static class PaymentGatewayOutcome
{
    public static void Apply(Payment payment, PaymentGatewayResult result)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(result);

        switch (result.Status)
        {
            case PaymentGatewayStatus.Approved:
                payment.AddTransaction(new PaymentTransaction(true, result.ExternalReference, result.Message));
                payment.Approve();
                break;
            case PaymentGatewayStatus.Rejected:
                payment.AddTransaction(new PaymentTransaction(false, result.ExternalReference, result.Message));
                payment.Reject(result.Message);
                break;
            case PaymentGatewayStatus.Pending:
                payment.AwaitCheckout(
                    result.ExternalReference,
                    result.CheckoutUrl ?? throw new InvalidOperationException("Pending gateway result must have a checkout URL."));
                break;
            default:
                throw new InvalidOperationException($"Unknown gateway status '{result.Status}'.");
        }
    }

    /// <summary>
    /// Aplica a confirmação vinda do gateway. Retorna false se o pagamento já estava decidido (webhook repetido)
    /// ou se o gateway ainda não decidiu.
    /// </summary>
    public static bool Confirm(Payment payment, GatewayPaymentStatus status)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(status);

        if (payment.Status != PaymentStatus.Pending || status.Status == PaymentGatewayStatus.Pending)
        {
            return false;
        }

        Apply(payment, new PaymentGatewayResult(status.Status, status.GatewayPaymentId, status.Message));
        return true;
    }
}

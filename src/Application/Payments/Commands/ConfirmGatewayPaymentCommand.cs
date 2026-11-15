using Application.Abstractions.Payments;
using Application.Abstractions.Persistence;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Payments.Commands;

/// <summary>
/// Confirma no billing o que o gateway decidiu. Chega pelo webhook do Mercado Pago (com o id do pagamento no
/// gateway) ou pela sincronização manual (com o id do Payment). Idempotente: notificação repetida não muda nada.
/// </summary>
public sealed record ConfirmGatewayPaymentCommand(string? GatewayPaymentId, Guid? PaymentId) : IRequest<ConfirmGatewayPaymentResult>;

public enum ConfirmGatewayPaymentResult
{
    /// <summary>Pagamento aprovado ou recusado agora.</summary>
    Updated,

    /// <summary>Gateway ainda não decidiu, ou o pagamento já estava decidido.</summary>
    Unchanged,

    /// <summary>O gateway não conhece o pagamento, ou ele não é deste billing.</summary>
    NotFound
}

public sealed class ConfirmGatewayPaymentCommandValidator : AbstractValidator<ConfirmGatewayPaymentCommand>
{
    public ConfirmGatewayPaymentCommandValidator()
    {
        RuleFor(x => x)
            .Must(command => !string.IsNullOrWhiteSpace(command.GatewayPaymentId) || command.PaymentId is { } id && id != Guid.Empty)
            .WithMessage("GatewayPaymentId or PaymentId is required.");
    }
}

public sealed class ConfirmGatewayPaymentCommandHandler(
    IPaymentGateway paymentGateway,
    IPaymentRepository paymentRepository,
    IUnitOfWork unitOfWork,
    ILogger<ConfirmGatewayPaymentCommandHandler> logger)
    : IRequestHandler<ConfirmGatewayPaymentCommand, ConfirmGatewayPaymentResult>
{
    public async Task<ConfirmGatewayPaymentResult> Handle(ConfirmGatewayPaymentCommand request, CancellationToken cancellationToken)
    {
        var status = !string.IsNullOrWhiteSpace(request.GatewayPaymentId)
            ? await paymentGateway.FindAsync(request.GatewayPaymentId.Trim(), cancellationToken)
            : await paymentGateway.FindByPaymentIdAsync(request.PaymentId!.Value, cancellationToken);

        if (status is null || request.PaymentId is { } expected && expected != status.PaymentId)
        {
            return ConfirmGatewayPaymentResult.NotFound;
        }

        var payment = await paymentRepository.GetByIdAsync(status.PaymentId, cancellationToken);
        if (payment is null)
        {
            logger.LogWarning(
                "Gateway payment {GatewayPaymentId} references unknown payment {PaymentId}.",
                status.GatewayPaymentId,
                status.PaymentId);
            return ConfirmGatewayPaymentResult.NotFound;
        }

        if (!PaymentGatewayOutcome.Confirm(payment, status))
        {
            return ConfirmGatewayPaymentResult.Unchanged;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Payment {PaymentId} {Status} by gateway payment {GatewayPaymentId}.",
            payment.Id,
            payment.Status,
            status.GatewayPaymentId);
        return ConfirmGatewayPaymentResult.Updated;
    }
}

using Application.Abstractions.Payments;
using Application.Abstractions.Persistence;
using Domain.Entities;
using MediatR;

namespace Application.Payments.Commands;

public sealed record ApprovePaymentCommand(Guid PaymentId, bool SimulateFailure) : IRequest;

public sealed class ApprovePaymentCommandHandler : IRequestHandler<ApprovePaymentCommand>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUnitOfWork _unitOfWork;

    public ApprovePaymentCommandHandler(
        IPaymentRepository paymentRepository,
        IPaymentGateway paymentGateway,
        IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _paymentGateway = paymentGateway;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(ApprovePaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.GetByIdAsync(request.PaymentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Payment '{request.PaymentId}' was not found.");

        var result = await _paymentGateway.ProcessAsync(
            payment.Id,
            payment.Amount,
            request.SimulateFailure,
            cancellationToken);

        payment.AddTransaction(new PaymentTransaction(result.IsSuccess, result.ExternalReference, result.Message));

        if (result.IsSuccess)
        {
            payment.Approve();
        }
        else
        {
            payment.Reject(result.Message);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

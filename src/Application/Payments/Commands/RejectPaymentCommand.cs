using Application.Abstractions.Persistence;
using FluentValidation;
using MediatR;

namespace Application.Payments.Commands;

public sealed record RejectPaymentCommand(Guid PaymentId, string Reason) : IRequest;

public sealed class RejectPaymentCommandValidator : AbstractValidator<RejectPaymentCommand>
{
    public RejectPaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class RejectPaymentCommandHandler : IRequestHandler<RejectPaymentCommand>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RejectPaymentCommandHandler(IPaymentRepository paymentRepository, IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(RejectPaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.GetByIdAsync(request.PaymentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Payment '{request.PaymentId}' was not found.");

        payment.Reject(request.Reason);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

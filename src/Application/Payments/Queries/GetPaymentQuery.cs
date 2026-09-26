using Application.Abstractions.Persistence;
using AutoMapper;
using FluentValidation;
using MediatR;

namespace Application.Payments.Queries;

public sealed record GetPaymentQuery(Guid PaymentId) : IRequest<PaymentResponse>;

public sealed class GetPaymentQueryValidator : AbstractValidator<GetPaymentQuery>
{
    public GetPaymentQueryValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
    }
}

public sealed class GetPaymentQueryHandler : IRequestHandler<GetPaymentQuery, PaymentResponse>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IMapper _mapper;

    public GetPaymentQueryHandler(IPaymentRepository paymentRepository, IMapper mapper)
    {
        _paymentRepository = paymentRepository;
        _mapper = mapper;
    }

    public async Task<PaymentResponse> Handle(GetPaymentQuery request, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.GetByIdAsync(request.PaymentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Payment '{request.PaymentId}' was not found.");

        return _mapper.Map<PaymentResponse>(payment);
    }
}

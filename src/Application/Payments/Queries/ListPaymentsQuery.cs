using Application.Abstractions.Persistence;
using AutoMapper;
using MediatR;

namespace Application.Payments.Queries;

public sealed record ListPaymentsQuery : IRequest<IReadOnlyCollection<PaymentSummaryResponse>>;

public sealed class ListPaymentsQueryHandler : IRequestHandler<ListPaymentsQuery, IReadOnlyCollection<PaymentSummaryResponse>>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IMapper _mapper;

    public ListPaymentsQueryHandler(IPaymentRepository paymentRepository, IMapper mapper)
    {
        _paymentRepository = paymentRepository;
        _mapper = mapper;
    }

    public async Task<IReadOnlyCollection<PaymentSummaryResponse>> Handle(ListPaymentsQuery request, CancellationToken cancellationToken)
    {
        var payments = await _paymentRepository.ListAsync(cancellationToken);
        return _mapper.Map<IReadOnlyCollection<PaymentSummaryResponse>>(payments);
    }
}

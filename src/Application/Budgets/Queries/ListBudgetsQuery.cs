using Application.Abstractions.Persistence;
using AutoMapper;
using MediatR;

namespace Application.Budgets.Queries;

public sealed record ListBudgetsQuery : IRequest<IReadOnlyCollection<BudgetSummaryResponse>>;

public sealed class ListBudgetsQueryHandler : IRequestHandler<ListBudgetsQuery, IReadOnlyCollection<BudgetSummaryResponse>>
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly IMapper _mapper;

    public ListBudgetsQueryHandler(IBudgetRepository budgetRepository, IMapper mapper)
    {
        _budgetRepository = budgetRepository;
        _mapper = mapper;
    }

    public async Task<IReadOnlyCollection<BudgetSummaryResponse>> Handle(ListBudgetsQuery request, CancellationToken cancellationToken)
    {
        var budgets = await _budgetRepository.ListAsync(cancellationToken);
        return _mapper.Map<IReadOnlyCollection<BudgetSummaryResponse>>(budgets);
    }
}

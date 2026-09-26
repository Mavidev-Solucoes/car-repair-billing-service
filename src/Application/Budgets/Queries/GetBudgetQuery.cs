using Application.Abstractions.Persistence;
using AutoMapper;
using FluentValidation;
using MediatR;

namespace Application.Budgets.Queries;

public sealed record GetBudgetQuery(Guid BudgetId) : IRequest<BudgetResponse>;

public sealed class GetBudgetQueryValidator : AbstractValidator<GetBudgetQuery>
{
    public GetBudgetQueryValidator()
    {
        RuleFor(x => x.BudgetId).NotEmpty();
    }
}

public sealed class GetBudgetQueryHandler : IRequestHandler<GetBudgetQuery, BudgetResponse>
{
    private readonly IBudgetRepository _budgetRepository;
    private readonly IMapper _mapper;

    public GetBudgetQueryHandler(IBudgetRepository budgetRepository, IMapper mapper)
    {
        _budgetRepository = budgetRepository;
        _mapper = mapper;
    }

    public async Task<BudgetResponse> Handle(GetBudgetQuery request, CancellationToken cancellationToken)
    {
        var budget = await _budgetRepository.GetByIdAsync(request.BudgetId, cancellationToken)
            ?? throw new KeyNotFoundException($"Budget '{request.BudgetId}' was not found.");

        return _mapper.Map<BudgetResponse>(budget);
    }
}

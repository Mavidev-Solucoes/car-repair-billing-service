using Application.Budgets.Queries;
using Application.Payments.Queries;
using AutoMapper;
using Domain.Entities;

namespace Application.Common.Mapping;

public sealed class QueryMappingProfile : Profile
{
    public QueryMappingProfile()
    {
        CreateMap<BudgetItem, BudgetItemResponse>()
            .ForCtorParam(nameof(BudgetItemResponse.Total), options => options.MapFrom(source => source.Total));

        CreateMap<Budget, BudgetResponse>()
            .ForCtorParam(nameof(BudgetResponse.TotalAmount), options => options.MapFrom(source => source.TotalAmount));

        CreateMap<Budget, BudgetSummaryResponse>()
            .ForCtorParam(nameof(BudgetSummaryResponse.TotalAmount), options => options.MapFrom(source => source.TotalAmount));

        CreateMap<PaymentTransaction, PaymentTransactionResponse>();

        CreateMap<Payment, PaymentResponse>();

        CreateMap<Payment, PaymentSummaryResponse>();
    }
}

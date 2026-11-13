using FluentValidation;
namespace Ims.Application.Features.Stock;

public sealed class QueryStockValidator : AbstractValidator<QueryStockQuery>
{
    public QueryStockValidator() => RuleFor(x => x.Filter.Search).MustBeAShortSearch();
}

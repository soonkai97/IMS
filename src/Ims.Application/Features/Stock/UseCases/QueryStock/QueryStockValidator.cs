using FluentValidation;
namespace Ims.Application.Features.Stock;

public class QueryStockValidator : AbstractValidator<QueryStockQuery>
{
    public QueryStockValidator() => RuleFor(x => x.Filter.Search).MustBeAShortSearch();
}

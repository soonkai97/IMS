using FluentValidation;
namespace Ims.Application.Features.Sales;

public class ListPriceListsValidator : AbstractValidator<ListPriceListsQuery>
{
    public ListPriceListsValidator() => RuleFor(x => x.Filter.Search).MustBeAShortSearch();
}

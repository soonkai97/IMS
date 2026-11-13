using FluentValidation;
namespace Ims.Application.Features.Sales;

public sealed class ListPriceListsValidator : AbstractValidator<ListPriceListsQuery>
{
    public ListPriceListsValidator() => RuleFor(x => x.Filter.Search).MustBeAShortSearch();
}

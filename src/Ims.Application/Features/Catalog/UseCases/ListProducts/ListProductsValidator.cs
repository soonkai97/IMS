using FluentValidation;
namespace Ims.Application.Features.Catalog;

public class ListProductsValidator : AbstractValidator<ListProductsQuery>
{
    public ListProductsValidator() => RuleFor(x => x.Filter.Search).MustBeAShortSearch();
}

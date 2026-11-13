using FluentValidation;
namespace Ims.Application.Features.Catalog;

public sealed class ListProductsValidator : AbstractValidator<ListProductsQuery>
{
    public ListProductsValidator() => RuleFor(x => x.Filter.Search).MustBeAShortSearch();
}

using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public class ListReorderSuggestionsValidator : AbstractValidator<ListReorderSuggestionsQuery>
{
    public ListReorderSuggestionsValidator()
    {
        RuleFor(x => x.Filter.WarehouseId).NotEmpty().WithMessage(Messages.WarehouseRequired);
        RuleFor(x => x.Filter.Search).MustBeAShortSearch();
        RuleFor(x => x.Filter.Search).MustBeLongEnoughToSearch();
    }
}

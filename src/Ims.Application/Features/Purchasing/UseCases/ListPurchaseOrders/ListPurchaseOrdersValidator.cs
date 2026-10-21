using FluentValidation;
namespace Ims.Application.Features.Purchasing;

public class ListPurchaseOrdersValidator : AbstractValidator<ListPurchaseOrdersQuery>
{
    public ListPurchaseOrdersValidator()
    {
        RuleFor(x => x.Filter.Search).MustBeAShortSearch();
        RuleFor(x => x.Filter.Search).MustBeLongEnoughToSearch();
        RuleFor(x => x.Filter.To).MustNotBeBefore(x => x.Filter.From);
    }
}

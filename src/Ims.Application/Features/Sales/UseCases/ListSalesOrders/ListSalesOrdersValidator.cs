using FluentValidation;
namespace Ims.Application.Features.Sales;

public class ListSalesOrdersValidator : AbstractValidator<ListSalesOrdersQuery>
{
    public ListSalesOrdersValidator()
    {
        RuleFor(x => x.Filter.Search).MustBeAShortSearch();
        RuleFor(x => x.Filter.Search).MustBeLongEnoughToSearch();
        RuleFor(x => x.Filter.To).MustNotBeBefore(x => x.Filter.From);
    }
}

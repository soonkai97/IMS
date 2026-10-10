using FluentValidation;
namespace Ims.Application.Features.Partners;

public class ListPartnersValidator : AbstractValidator<ListPartnersQuery>
{
    public ListPartnersValidator() => RuleFor(x => x.Filter.Search).MustBeAShortSearch();
}

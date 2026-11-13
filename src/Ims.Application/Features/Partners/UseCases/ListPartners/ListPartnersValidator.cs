using FluentValidation;
namespace Ims.Application.Features.Partners;

public sealed class ListPartnersValidator : AbstractValidator<ListPartnersQuery>
{
    public ListPartnersValidator() => RuleFor(x => x.Filter.Search).MustBeAShortSearch();
}

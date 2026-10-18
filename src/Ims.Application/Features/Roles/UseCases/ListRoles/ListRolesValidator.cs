using FluentValidation;
namespace Ims.Application.Features.Roles;

public class ListRolesValidator : AbstractValidator<ListRolesQuery>
{
    public ListRolesValidator() => RuleFor(x => x.Filter.Search).MustBeAShortSearch();
}

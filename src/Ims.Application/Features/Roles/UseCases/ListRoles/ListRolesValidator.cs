using FluentValidation;
namespace Ims.Application.Features.Roles;

public sealed class ListRolesValidator : AbstractValidator<ListRolesQuery>
{
    public ListRolesValidator() => RuleFor(x => x.Filter.Search).MustBeAShortSearch();
}

using FluentValidation;
namespace Ims.Application.Features.Users;

public sealed class ListUsersValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersValidator() => RuleFor(x => x.Filter.Search).MustBeAShortSearch();
}

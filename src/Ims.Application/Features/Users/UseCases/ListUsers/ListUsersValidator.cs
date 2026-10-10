using FluentValidation;
namespace Ims.Application.Features.Users;

public class ListUsersValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersValidator() => RuleFor(x => x.Filter.Search).MustBeAShortSearch();
}

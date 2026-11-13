using FluentValidation;
namespace Ims.Application.Features.Users;

public sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator() => RuleFor(x => x.Data).SetValidator(new UserSaveRequestValidator(creating: true));
}

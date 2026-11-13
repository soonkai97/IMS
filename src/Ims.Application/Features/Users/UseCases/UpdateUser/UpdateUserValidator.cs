using FluentValidation;
namespace Ims.Application.Features.Users;

public sealed class UpdateUserValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserValidator() => RuleFor(x => x.Data).SetValidator(new UserSaveRequestValidator(creating: false));
}

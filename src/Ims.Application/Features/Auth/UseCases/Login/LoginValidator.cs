using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Auth;

public class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        // Over-long input gets the generic credentials message: nothing is revealed and no hashing work is done for it.
        RuleFor(x => x.Body.Username).NotEmpty().WithMessage(Messages.UsernameRequired)
            .MaximumLength(Limits.EmailMaxLength).WithMessage(Messages.InvalidCredentials);
        RuleFor(x => x.Body.Password).NotEmpty().WithMessage(Messages.PasswordRequired)
            .MaximumLength(Limits.PasswordMaxLength).WithMessage(Messages.InvalidCredentials);
    }
}

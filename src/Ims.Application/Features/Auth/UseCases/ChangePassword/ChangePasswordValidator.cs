using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Auth;

public class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.Body.CurrentPassword).NotEmpty().WithMessage(Messages.PasswordRequired)
            .MaximumLength(Limits.PasswordMaxLength).WithMessage(Messages.CurrentPasswordWrong);
        RuleFor(x => x.Body.NewPassword).NotEmpty().WithMessage(Messages.PasswordRequired)
            .MinimumLength(Limits.PasswordMinLength).WithMessage(Messages.PasswordTooShort)
            .MaximumLength(Limits.PasswordMaxLength).WithMessage(Messages.PasswordTooShort);
        RuleFor(x => x).Must(x => x.Body.NewPassword != x.Body.CurrentPassword).WithMessage(Messages.PasswordUnchanged);
    }
}

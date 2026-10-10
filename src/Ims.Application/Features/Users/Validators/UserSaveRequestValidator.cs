using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Users;

public class UserSaveRequestValidator : AbstractValidator<UserSaveRequest>
{
    public UserSaveRequestValidator(bool creating)
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(Limits.UsernameMaxLength).WithMessage(Messages.UsernameInvalid);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(Limits.EmailMaxLength).WithMessage(Messages.EmailInvalid);
        RuleFor(x => x.DisplayName).MaximumLength(Limits.ShortNameMaxLength);
        RuleFor(x => x.Role).Must(r => Roles.All.Contains(r)).WithMessage(Messages.UnknownRole);
        if (creating)
            RuleFor(x => x.Password).NotEmpty().MinimumLength(Limits.PasswordMinLength).MaximumLength(Limits.PasswordMaxLength).WithMessage(Messages.PasswordTooShort);
        else
            RuleFor(x => x.Password).MinimumLength(Limits.PasswordMinLength).When(x => !string.IsNullOrEmpty(x.Password)).WithMessage(Messages.PasswordTooShort);
    }
}

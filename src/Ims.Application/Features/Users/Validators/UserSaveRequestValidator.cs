using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Users;

public class UserSaveRequestValidator : AbstractValidator<UserSaveRequest>
{
    /// <summary>Letters, digits and the few symbols an e-mail style user name needs; no spaces, no control or look-alike characters.</summary>
    private static readonly System.Text.RegularExpressions.Regex AllowedUsername = new(@"^[A-Za-z0-9._@+\-]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public UserSaveRequestValidator(bool creating)
    {
        RuleFor(x => x.Username).NotEmpty().WithMessage(Messages.UsernameInvalid)
            .MaximumLength(Limits.UsernameMaxLength).WithMessage(Messages.TextTooLong("Username", Limits.UsernameMaxLength))
            .Must(name => string.IsNullOrEmpty(name) || AllowedUsername.IsMatch(name)).WithMessage(Messages.UsernameCharacters);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(Limits.EmailMaxLength).WithMessage(Messages.EmailInvalid);
        RuleFor(x => x.DisplayName).MaximumLength(Limits.ShortNameMaxLength);
        RuleFor(x => x.Role).Must(r => Roles.All.Contains(r)).WithMessage(Messages.UnknownRole);
        if (creating)
            RuleFor(x => x.Password).NotEmpty().MinimumLength(Limits.PasswordMinLength).MaximumLength(Limits.PasswordMaxLength).WithMessage(Messages.PasswordTooShort);
        else
            RuleFor(x => x.Password).MinimumLength(Limits.PasswordMinLength).When(x => !string.IsNullOrEmpty(x.Password)).WithMessage(Messages.PasswordTooShort);
    }
}

using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Roles;

public class UpdateRoleValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleValidator()
    {
        RuleFor(x => x.Data.Name).NotEmpty().Must(RoleTextRules.IsAValidName).WithMessage(Messages.RoleNameInvalid);
        RuleFor(x => x.Data.Description).MaximumLength(Limits.RoleDescriptionMaxLength).WithMessage(Messages.RoleDescriptionTooLong);
        RuleFor(x => x.Data.Name).Must(n => Role.IsPlainText(n ?? "")).WithMessage(Messages.RoleTextNotPlain);
        RuleFor(x => x.Data.Description).Must(d => Role.IsPlainText(d ?? "")).WithMessage(Messages.RoleTextNotPlain);
    }
}

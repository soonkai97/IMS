using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Roles;

public class UpdateRoleValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleValidator()
    {
        RuleFor(x => x.Data.Name).NotEmpty().Must(RoleTextRules.IsAValidName).WithMessage(Messages.RoleNameInvalid);
        RuleFor(x => x.Data.Description).MaximumLength(Limits.RoleDescriptionMaxLength).WithMessage(Messages.RoleDescriptionTooLong);
    }
}

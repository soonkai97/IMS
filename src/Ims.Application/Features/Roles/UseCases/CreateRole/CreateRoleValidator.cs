using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Roles;

public class CreateRoleValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleValidator()
    {
        RuleFor(x => x.Data.Name).NotEmpty().Must(RoleTextRules.IsAValidName).WithMessage(Messages.RoleNameInvalid);
        RuleFor(x => x.Data.Description).MaximumLength(Limits.RoleDescriptionMaxLength).WithMessage(Messages.RoleDescriptionTooLong);
        RuleFor(x => x.Data.Permissions).MustBeAValidPermissionSet();
        RuleFor(x => x.Data).Must(d => d.CopyFromRoleId is null || d.Permissions is null).WithMessage(Messages.RoleCopyAndPermissions);
    }
}

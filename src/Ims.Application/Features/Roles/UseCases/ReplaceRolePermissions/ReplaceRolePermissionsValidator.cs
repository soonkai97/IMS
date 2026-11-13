using FluentValidation;
namespace Ims.Application.Features.Roles;

public sealed class ReplaceRolePermissionsValidator : AbstractValidator<ReplaceRolePermissionsCommand>
{
    public ReplaceRolePermissionsValidator() => RuleFor(x => x.Data.Permissions).NotNull().MustBeAValidPermissionSet();
}

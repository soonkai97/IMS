namespace Ims.Application.Features.Roles;

public record CreateRoleCommand(RoleCreateRequest Data) : ICommand<RoleDto>;

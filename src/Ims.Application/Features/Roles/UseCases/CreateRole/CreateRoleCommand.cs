namespace Ims.Application.Features.Roles;

public sealed record CreateRoleCommand(RoleCreateRequest Data) : ICommand<RoleDto>;

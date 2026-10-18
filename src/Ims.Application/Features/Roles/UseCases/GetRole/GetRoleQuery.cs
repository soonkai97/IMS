namespace Ims.Application.Features.Roles;

public record GetRoleQuery(Guid Id) : IQuery<RoleDto>;

namespace Ims.Application.Features.Roles;

public sealed record GetRoleQuery(Guid Id) : IQuery<RoleDto>;

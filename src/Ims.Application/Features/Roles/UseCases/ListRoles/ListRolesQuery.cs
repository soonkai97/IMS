namespace Ims.Application.Features.Roles;

public sealed record ListRolesQuery(RoleFilter Filter) : IQuery<IReadOnlyList<RoleDto>>;

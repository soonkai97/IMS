namespace Ims.Application.Features.Roles;

public record ListRolesQuery(RoleFilter Filter) : IQuery<IReadOnlyList<RoleDto>>;

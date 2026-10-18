namespace Ims.Application.Features.Roles;

public class ListRolesHandler(IRoleReader roles) : IQueryHandler<ListRolesQuery, IReadOnlyList<RoleDto>>
{
    public Task<IReadOnlyList<RoleDto>> HandleAsync(ListRolesQuery q, CancellationToken ct) => roles.ListAsync(q.Filter, ct);
}

using Ims.Domain;
namespace Ims.Application.Features.Users;

/// <summary>
/// The active roles the caller may give to a user: the same rule as the user save (<see cref="UserContainment.CheckAssignment"/>, the caller holds every permission of the role), so the
/// user form never offers a role the save would refuse. It needs <c>users.manage</c>, not <c>roles.read</c>: a user manager may not be allowed to read the roles screen.
/// </summary>
public class ListAssignableRolesHandler(IReadRepository<Role> roles, ICurrentUser current) : IQueryHandler<ListAssignableRolesQuery, IReadOnlyList<AssignableRoleDto>>
{
    public async Task<IReadOnlyList<AssignableRoleDto>> HandleAsync(ListAssignableRolesQuery q, CancellationToken ct)
    {
        var rights = current.Rights();
        var all = await roles.ListAsync(new AssignableRolesSpec(), ct);
        return all.Where(r => rights.HoldsAll(r.Grants.Select(g => g.PermissionCode))).Select(r => new AssignableRoleDto(r.Code, r.Name)).ToList();
    }
}

using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Users;

/// <summary>
/// The active users whose role administers the system (live, and holding the whole administration core, system or custom: <see cref="User.IsAdministrator"/>), optionally without one user
/// or without the holders of one role: "who is left once this write is done". The same rule as `Role.Administers`, written as a query (it cannot call the domain property). A role that is soft-deleted drops out through its query filter, so its holders do not count.
/// </summary>
public sealed class ActiveAdministratorsSpec : Specification<User>
{
    private static readonly string[] Core = [.. PermissionCatalog.AdminCoreCodes];

    public ActiveAdministratorsSpec(int exceptUserId = 0, int exceptRoleId = 0) =>
        Query.AsNoTracking().Where(u => u.IsActive && u.Role.IsActive && u.Id != exceptUserId && u.RoleId != exceptRoleId
            && Core.All(code => u.Role.Grants.Any(g => g.PermissionCode == code)));
}

using Ims.Domain;
namespace Ims.Application.Features.Auth;

/// <summary>
/// Two reads by primary key: the active user (role id, scope) and the user's role with its grants. The permissions come from <see cref="EffectivePermissions.Compute"/>,
/// the one place that turns grants into rights. A missing or inactive user, or a role that is gone, gives no snapshot, and no snapshot means no access.
/// </summary>
public sealed class AccessSnapshotLoader(IReadRepository<User> users, IReadRepository<Role> roles) : IAccessSnapshotLoader, IAppService
{
    public async Task<AccessSnapshot?> LoadAsync(int userId, CancellationToken ct)
    {
        var user = await users.FirstOrDefaultAsync(new AccessUserSpec(userId), ct);
        if (user is null) return null;
        var role = await roles.FirstOrDefaultAsync(new AccessRoleSpec(user.RoleId), ct);
        if (role is null) return null;
        return new AccessSnapshot(userId, role.Code, role.Name, EffectivePermissions.Compute(role.PermissionCodes), user.AllWarehouses, user.WarehouseIds);
    }
}

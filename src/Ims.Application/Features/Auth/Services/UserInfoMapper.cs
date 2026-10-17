using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Auth;

public static class UserInfoMapper
{
    /// <summary>
    /// The user's warehouses leave as public ids (one query for the list); the data scope itself uses the integer ids. The role's name and the scope flag are read
    /// fresh from the loader (not the cache), so the person sees what they hold now. A user the loader does not know is no longer signed in.
    /// </summary>
    public static async Task<UserInfo> ToInfoAsync(User u, IIdMap<Warehouse> warehouses, IAccessSnapshotLoader loader, CancellationToken ct)
    {
        var access = await loader.LoadAsync(u.Id, ct) ?? throw new UnauthorizedException(Messages.SessionExpired);
        var publicIds = await warehouses.ToPublicAsync(u.Warehouses.Select(w => w.WarehouseId), ct);
        return new(u.PublicId, u.Username, u.DisplayName, u.Role, access.RoleName, Permissions.For(u.Role).ToList(), access.AllWarehouses,
            u.Warehouses.Select(w => publicIds[w.WarehouseId]).ToList());
    }
}

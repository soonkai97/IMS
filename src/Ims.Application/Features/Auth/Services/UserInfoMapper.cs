using Ims.Domain;
namespace Ims.Application.Features.Auth;

public static class UserInfoMapper
{
    /// <summary>
    /// Sign-in, refresh and /me say the same thing about the rights because they all come here with the role, the permissions and the scope of one access token: the
    /// snapshot it was written from, or the token's own claims. A user who sees every warehouse lists none (as the token does); the others leave as public ids (one query
    /// for the list). <c>Permissions</c> lists the codes of the token, in catalog order.
    /// </summary>
    public static async Task<UserInfo> ToInfoAsync(User u, string roleCode, string roleName, EffectivePermissions permissions, bool allWarehouses,
        IReadOnlyCollection<int> warehouseIds, IIdMap<Warehouse> warehouses, IReadOnlyList<string> modules, CancellationToken ct)
    {
        var publicIds = await warehouses.ToPublicAsync(allWarehouses ? [] : warehouseIds, ct);
        // Why only Returns: the web shows the older modules (purchasing, sales, ...) by module AND permission and PL3.15 pinned that their grants stay listed while the module is off (ModuleAwareRolesTests), so widening this to every module
        // (RoleReader does list through ModulePermissions.Offered) is a contract decision for the architect. Returns is new and nothing reads its grant while it is off, so a client is never offered returns.create then; the grant stays on the role and in the token.
        return new(u.PublicId, u.Username, u.DisplayName, roleCode, roleName, permissions.Codes.Where(c => PermissionCatalog.Find(c)?.Module != ModuleCodes.Returns || ModulePermissions.IsOffered(c, modules)).ToList(), allWarehouses,
            (allWarehouses ? [] : warehouseIds).Select(id => publicIds[id]).ToList(), modules);
    }
}

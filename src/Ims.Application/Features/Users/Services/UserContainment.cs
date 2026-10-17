using Ims.Domain;
namespace Ims.Application.Features.Users;

/// <summary>
/// Nobody can use the user save to get more than they have (ADR roles-permissions 2.6 and 2.9, "no escalation"): a caller works only on users who do not see more than
/// they do, gives only roles whose permissions they hold, and assigns only warehouses they can reach themselves, themselves included. An administrator who sees every
/// warehouse and holds every permission passes every check, so nothing changes for them.
/// </summary>
internal static class UserContainment
{
    /// <summary>The role named by the request exactly: the database collation also matches another case, a trailing space or a look-alike.</summary>
    /// <exception cref="DomainException">No such role.</exception>
    public static Role ExactRole(Role? found, string requested) =>
        found is not null && string.Equals(found.Code, requested, StringComparison.Ordinal) ? found : throw new DomainException(Messages.UnknownRole);

    /// <summary>Editing, resetting the password of or deactivating a user who sees more warehouses than the caller or holds permissions the caller lacks is a takeover.</summary>
    /// <exception cref="ForbiddenException">The user has more access than the caller.</exception>
    public static void CheckTarget(User target, ICurrentUser caller)
    {
        var rights = caller.Rights();
        var sawMore = (target.AllWarehouses && !caller.AllWarehouses)
                      || (!target.AllWarehouses && target.Warehouses.Any(w => !caller.CanAccessWarehouse(w.WarehouseId)))
                      || target.Role.Grants.Any(g => !rights.Has(g.PermissionCode));
        if (sawMore) throw new ForbiddenException(Messages.UserBeyondYourAccess);
    }

    /// <summary>The role and the warehouses a save would give.</summary>
    /// <exception cref="ForbiddenException">The role holds a permission the caller lacks, or a warehouse is outside the caller's scope.</exception>
    public static void CheckAssignment(Role role, IReadOnlyCollection<int> warehouseIds, ICurrentUser caller)
    {
        var rights = caller.Rights();
        if (role.Grants.Any(g => !rights.Has(g.PermissionCode))) throw new ForbiddenException(Messages.RoleBeyondYourAccess);
        if (warehouseIds.Any(id => !caller.CanAccessWarehouse(id))) throw new ForbiddenException(Messages.WarehouseOutsideYourScope);
    }
}

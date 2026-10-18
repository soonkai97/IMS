using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>
/// Nobody uses the role writes to get more than they have (ADR roles-permissions 2.9, "no escalation", the same rail <see cref="Users.UserContainment"/> puts on the user save): a caller
/// gives a role only permissions they hold, changes only a role that holds nothing they lack, and never changes the role they hold themselves. A role carries no warehouse scope
/// (scope is a choice per person), so permissions are the whole of what a role can give. An administrator, who holds every permission, passes the first two checks.
/// </summary>
internal static class RoleContainment
{
    /// <summary>Giving a permission the caller does not hold would let a role manager lend themselves, or anyone, more than they have.</summary>
    /// <exception cref="ForbiddenException">A permission is not held by the caller.</exception>
    public static void CheckGrantable(IEnumerable<string> permissions, ICurrentUser caller)
    {
        var rights = caller.Rights();
        if (!rights.HoldsAll(permissions)) throw new ForbiddenException(Messages.GrantBeyondYourAccess);
    }
    /// <summary>
    /// Changing or deleting a role that holds more than the caller is a takeover of that access; changing the caller's own role is self-service on their own rights; and a role held by
    /// someone who sees more warehouses than the caller is a way to change what that person may do, which the user save refuses the caller too (<see cref="Users.UserContainment.CheckTarget"/>).
    /// </summary>
    /// <exception cref="ConflictException">The role is the caller's own.</exception>
    /// <exception cref="ForbiddenException">The role holds a permission the caller lacks, or a user who sees more than the caller holds it.</exception>
    public static async Task CheckEditableAsync(Role role, IReadRepository<User> users, ICurrentUser caller, CancellationToken ct)
    {
        // The role the caller holds now, from the database (the role claim of the token can be a few minutes old).
        var callerRoleId = await users.FirstOrDefaultAsync(new RoleIdOfUserSpec(caller.UserId), ct);
        if (role.Id == callerRoleId || string.Equals(role.Code, caller.Role, StringComparison.Ordinal)) throw new ConflictException(Messages.CannotChangeOwnRole);
        var rights = caller.Rights();
        if (!rights.HoldsAll(role.Grants.Select(g => g.PermissionCode))) throw new ForbiddenException(Messages.RoleEditBeyondYourAccess);
        if (caller.AllWarehouses) return;
        var holders = await users.ListAsync(new UsersOfRoleSpec(role.Id), ct);
        if (holders.Any(u => u.AllWarehouses || u.Warehouses.Any(w => !caller.CanAccessWarehouse(w.WarehouseId)))) throw new ForbiddenException(Messages.RoleHeldBeyondYourScope);
    }
}

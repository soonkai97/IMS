namespace Ims.Domain;

/// <summary>
/// What the system knows about one active user right now, for rights: the role (by code, for display and audit), the permissions that role holds and the data
/// scope. Immutable: the loader builds it, the cache shares it between the requests of the user. There is no snapshot for a user who is missing or inactive, and
/// no snapshot means no access (ADR roles-permissions 3.2).
/// </summary>
public sealed record AccessSnapshot(
    int UserId, string RoleCode, string RoleName, EffectivePermissions Permissions, bool AllWarehouses, IReadOnlyList<int> WarehouseIds)
{
    public bool Has(string permission) => Permissions.Has(permission);
}

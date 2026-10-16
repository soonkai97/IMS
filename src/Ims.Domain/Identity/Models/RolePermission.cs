namespace Ims.Domain;

/// <summary>
/// One permission held by one role. Unique per role among live rows; a revoked permission is a soft-deleted row. No public id: nothing addresses a single
/// grant (a client replaces the whole set of a role), like the other child rows (ADR guid-keys 4.3).
/// </summary>
public class RolePermission : Entity
{
    private RolePermission() { }

    public int RoleId { get; private set; }

    /// <summary>A code of <see cref="PermissionCatalog"/>, at most <see cref="Limits.PermissionCodeMaxLength"/> characters.</summary>
    public string PermissionCode { get; private set; } = "";

    internal static RolePermission For(string permissionCode) => new() { PermissionCode = permissionCode };
}

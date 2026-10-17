namespace Ims.Domain;

/// <summary>
/// A named set of permissions that a user holds (ADR roles-permissions 2.2, lean scope: one role per user). The five system roles are seeded by the migration with
/// the permissions of <see cref="SystemRoleDefaults"/>; the code is the stable text a role is known by and never
/// changes. The permissions are code strings of <see cref="PermissionCatalog"/>, not rows of a permission table.
/// </summary>
public class Role : PublicEntity
{
    private Role() { }

    /// <summary>Stable and unique among live roles; the system roles keep the strings of <see cref="Roles"/>.</summary>
    public string Code { get; private set; } = "";
    /// <summary>What a person reads; unique among live roles.</summary>
    public string Name { get; private set; } = "";
    public string Description { get; private set; } = "";
    /// <summary>A system role is part of the installation: it can be neither deleted nor deactivated.</summary>
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; } = true;

    private readonly List<RolePermission> _grants = [];

    /// <summary>The permissions the role holds, one row each (the live rows: a revoked grant is a soft-deleted row and is not loaded).</summary>
    public IReadOnlyList<RolePermission> Grants => _grants.Where(g => !g.RecDeleted).ToList();

    public static Role Create(string code, string name, string? description, bool isSystem, IEnumerable<string> permissionCodes)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > Limits.RoleMaxLength) throw new DomainException(Messages.RoleCodeInvalid);
        var role = new Role { Code = code.Trim(), IsSystem = isSystem };
        role.UpdateText(name, description);
        foreach (var permission in permissionCodes.Distinct(StringComparer.Ordinal))
        {
            if (!PermissionCatalog.IsKnown(permission)) throw new DomainException(Messages.UnknownPermission(permission));
            role._grants.Add(RolePermission.For(permission));
        }
        return role;
    }

    /// <summary>Renames the role and changes its description. The code never changes.</summary>
    public void UpdateText(string name, string? description)
    {
        var trimmedName = (name ?? "").Trim();
        if (trimmedName.Length is < Limits.RoleNameMinLength or > Limits.RoleNameMaxLength) throw new DomainException(Messages.RoleNameInvalid);
        var text = (description ?? "").Trim();
        if (text.Length > Limits.RoleDescriptionMaxLength) throw new DomainException(Messages.RoleDescriptionTooLong);
        Name = trimmedName;
        Description = text;
    }

    /// <summary>An inactive role cannot be given to anyone new. A system role is always active.</summary>
    public void SetActive(bool active)
    {
        if (!active && IsSystem) throw new DomainException(Messages.SystemRoleProtected);
        IsActive = active;
    }

    /// <summary>
    /// Replaces the whole set: grants that are no longer wanted are revoked (soft-deleted), new codes are added, the rest is left alone. Every code must be one of the catalog.
    /// </summary>
    /// <returns>The codes added and the codes removed, each in catalog order; both empty when the set did not change.</returns>
    public (IReadOnlyList<string> Added, IReadOnlyList<string> Removed) SetPermissions(IEnumerable<string> permissionCodes)
    {
        var wanted = permissionCodes.ToHashSet(StringComparer.Ordinal);
        var unknown = wanted.FirstOrDefault(c => !PermissionCatalog.IsKnown(c));
        if (unknown is not null) throw new DomainException(Messages.UnknownPermission(unknown));

        var held = Grants;
        var removed = held.Where(g => !wanted.Contains(g.PermissionCode)).ToList();
        foreach (var grant in removed) grant.MarkDeleted();
        var added = wanted.Except(held.Select(g => g.PermissionCode), StringComparer.Ordinal).ToList();
        foreach (var code in added) _grants.Add(RolePermission.For(code));

        return (InCatalogOrder(added), InCatalogOrder(removed.Select(g => g.PermissionCode)));
    }

    /// <summary>Deletes the role (soft). Its grants stay as rows, hidden with it. Whether anyone still holds it is the caller's question to ask first.</summary>
    public void Delete()
    {
        if (IsSystem) throw new DomainException(Messages.SystemRoleProtected);
        MarkDeleted();
    }

    private static IReadOnlyList<string> InCatalogOrder(IEnumerable<string> codes) => PermissionCatalog.InOrder(codes);
}

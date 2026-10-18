using System.Globalization;
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

    /// <summary>
    /// A system role that holds every admin-core permission of the catalog always holds all it has, so the system can always be administered: its permission set cannot be replaced. It is a fact
    /// of the role's data, not a name (the seeded Administrator role is the one that qualifies).
    /// </summary>
    public bool PermissionsLocked => IsSystem && PermissionCatalog.All.Where(p => p.AdminCore).All(p => Grants.Any(g => g.PermissionCode == p.Code));

    /// <summary>The permissions the role holds, one row each (the live rows: a revoked grant is a soft-deleted row and is not loaded).</summary>
    public IReadOnlyList<RolePermission> Grants => _grants.Where(g => !g.RecDeleted).ToList();

    /// <summary>
    /// Plain text: no control character (NUL, CR, LF, tab) and no invisible format or separator character (the direction overrides U+202A to U+202E and U+2066 to U+2069 that make a
    /// name read as another, zero-width characters, line and paragraph separators). A name and a description are shown in lists and in audit rows.
    /// </summary>
    public static bool IsPlainText(string text) => text.All(c => !char.IsControl(c) &&
        CharUnicodeInfo.GetUnicodeCategory(c) is not (UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator));

    public static Role Create(string code, string name, string? description, bool isSystem, IEnumerable<string> permissionCodes)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > Limits.RoleMaxLength) throw new DomainException(Messages.RoleCodeInvalid);
        var role = new Role { Code = code.Trim(), IsSystem = isSystem };
        role.SetText(name, description);
        foreach (var permission in permissionCodes.Distinct(StringComparer.Ordinal))
        {
            if (!PermissionCatalog.IsKnown(permission)) throw new DomainException(Messages.UnknownPermission(permission));
            role._grants.Add(RolePermission.For(permission));
        }
        return role;
    }

    /// <summary>Renames the role and changes its description; a system role keeps its name and text. The code never changes.</summary>
    public void UpdateText(string name, string? description)
    {
        if (IsSystem) throw new LockedRoleException(Messages.SystemRoleProtected);
        SetText(name, description);
    }

    private void SetText(string name, string? description)
    {
        if (!IsPlainText(name ?? "") || !IsPlainText(description ?? "")) throw new DomainException(Messages.RoleTextNotPlain);
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
        if (!active && IsSystem) throw new LockedRoleException(Messages.SystemRoleProtected);
        IsActive = active;
    }

    /// <summary>
    /// Replaces the whole set (refused for a role whose permissions are locked): grants that are no longer wanted are revoked (soft-deleted), new codes are added, the rest is left alone. Every code must be one of the catalog.
    /// </summary>
    /// <returns>The codes added and the codes removed, each in catalog order; both empty when the set did not change.</returns>
    public (IReadOnlyList<string> Added, IReadOnlyList<string> Removed) SetPermissions(IEnumerable<string> permissionCodes)
    {
        if (PermissionsLocked) throw new LockedRoleException(Messages.AdminRoleLocked);
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
        if (IsSystem) throw new LockedRoleException(Messages.SystemRoleProtected);
        MarkDeleted();
    }

    private static IReadOnlyList<string> InCatalogOrder(IEnumerable<string> codes) => PermissionCatalog.InOrder(codes);
}

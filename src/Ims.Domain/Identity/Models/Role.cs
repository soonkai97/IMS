namespace Ims.Domain;

/// <summary>
/// A named set of permissions that a user holds (ADR roles-permissions 2.2, lean scope: one role per user). The five system roles are seeded by the migration with
/// the permissions of <see cref="SystemRoleDefaults"/>; the code is the stable text a role is known by (the same string <see cref="User.Role"/> holds today) and never
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

    /// <summary>The permissions the role holds, one row each.</summary>
    public IReadOnlyList<RolePermission> Grants => _grants;

    public static Role Create(string code, string name, string? description, bool isSystem, IEnumerable<string> permissionCodes)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > Limits.RoleMaxLength) throw new DomainException(Messages.RoleCodeInvalid);
        var trimmedName = (name ?? "").Trim();
        if (trimmedName.Length is < Limits.RoleNameMinLength or > Limits.RoleNameMaxLength) throw new DomainException(Messages.RoleNameInvalid);
        var text = (description ?? "").Trim();
        if (text.Length > Limits.RoleDescriptionMaxLength) throw new DomainException(Messages.RoleDescriptionTooLong);

        var role = new Role { Code = code.Trim(), Name = trimmedName, Description = text, IsSystem = isSystem };
        foreach (var permission in permissionCodes.Distinct(StringComparer.Ordinal))
        {
            if (!PermissionCatalog.IsKnown(permission)) throw new DomainException(Messages.UnknownPermission(permission));
            role._grants.Add(RolePermission.For(permission));
        }
        return role;
    }
}

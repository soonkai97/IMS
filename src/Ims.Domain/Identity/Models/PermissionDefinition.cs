namespace Ims.Domain;

/// <summary>
/// One entry of the permission catalog (<see cref="PermissionCatalog"/>). The catalog is code: a permission is never renamed once released.
/// </summary>
/// <param name="Code">Stable lower-case dotted code, at most <see cref="Limits.PermissionCodeMaxLength"/> characters; the constant in <see cref="Permissions"/> names it.</param>
/// <param name="Group">The section of the role editor it is listed in.</param>
/// <param name="Description">One plain sentence for the role editor.</param>
public sealed class PermissionDefinition(string Code, PermissionGroup Group, string Description)
{
    public string Code { get; } = Code;
    public PermissionGroup Group { get; } = Group;
    public string Description { get; } = Description;

    /// <summary>Granting it needs an administrator and a reason (enforced from the role editor slices on).</summary>
    public bool IsDangerous { get; init; }

    /// <summary>Part of the administrability core: the Admin role always holds it, so the system can always be administered.</summary>
    public bool AdminCore { get; init; }

    /// <summary>Codes that must be granted together with this one.</summary>
    public IReadOnlyList<string> Requires { get; init; } = [];
}

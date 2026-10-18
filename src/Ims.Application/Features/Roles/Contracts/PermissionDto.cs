using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>One permission of the catalog, as the role editor lists it.</summary>
/// <param name="Requires">Codes that must be granted together with this one (a role that has this code without them is refused).</param>
/// <param name="IsDangerous">Granting it deserves a warning.</param>
/// <param name="AdminCore">The Administrator role always holds it.</param>
public record PermissionDto(string Code, PermissionGroup Group, string Description, bool IsDangerous, bool AdminCore, IReadOnlyList<string> Requires);

namespace Ims.Application.Features.Roles;

/// <summary>The whole set of permissions the role is to hold (a replace, not a patch).</summary>
public record RolePermissionsRequest(IReadOnlyList<string> Permissions);

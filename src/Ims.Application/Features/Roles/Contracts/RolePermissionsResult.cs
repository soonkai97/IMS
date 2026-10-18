namespace Ims.Application.Features.Roles;

/// <summary>The role after a replace, and what the replace changed (both empty when the set was the same).</summary>
public record RolePermissionsResult(RoleDto Role, IReadOnlyList<string> Added, IReadOnlyList<string> Removed);

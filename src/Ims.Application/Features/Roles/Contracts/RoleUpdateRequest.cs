namespace Ims.Application.Features.Roles;

/// <summary>The text and the state of a role. Its code, its kind and its permissions are not here.</summary>
public record RoleUpdateRequest(string Name, string? Description, bool IsActive);

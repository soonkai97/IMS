namespace Ims.Application.Features.Roles;

/// <summary>The text and the state of a role. Its code, its kind and its permissions are not here. A description or state that is left out stays as it is.</summary>
public record RoleUpdateRequest(string Name, string? Description = null, bool? IsActive = null);

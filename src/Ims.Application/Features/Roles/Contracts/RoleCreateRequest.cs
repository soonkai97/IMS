namespace Ims.Application.Features.Roles;

/// <summary>A new custom role. The code is made by the server from the name. Give <paramref name="CopyFromRoleId"/> or <paramref name="Permissions"/>, not both; neither makes a blank role.</summary>
/// <param name="CopyFromRoleId">Takes the permissions the other role holds now (no link is kept).</param>
public record RoleCreateRequest(string Name, string? Description, Guid? CopyFromRoleId = null, IReadOnlyList<string>? Permissions = null);

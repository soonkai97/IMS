namespace Ims.Application.Features.Auth;

/// <summary>What the access snapshot needs from the user's role: its names and the codes of the permissions it holds.</summary>
public sealed record AccessRoleRow(string Code, string Name, IReadOnlyList<string> PermissionCodes);

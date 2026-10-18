namespace Ims.Application.Features.Roles;

/// <summary>A role as the database holds it, before the permissions are put in catalog order and the token is made.</summary>
public sealed record RoleRow(int Id, Guid PublicId, string Code, string Name, string Description, bool IsSystem, bool IsActive, byte[] RowVersion, IReadOnlyList<string> Permissions);

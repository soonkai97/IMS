namespace Ims.Application.Features.Roles;

/// <param name="IfMatch">The concurrency token the caller last saw; null (If-Match: *) skips the check. The API refuses a request that carries none.</param>
public record ReplaceRolePermissionsCommand(Guid Id, RolePermissionsRequest Data, string? IfMatch) : ICommand<RolePermissionsResult>;

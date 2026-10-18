namespace Ims.Application.Features.Roles;

/// <param name="IfMatch">The concurrency token the caller last saw; null skips the check.</param>
public record UpdateRoleCommand(Guid Id, RoleUpdateRequest Data, string? IfMatch = null) : ICommand<RoleDto>;

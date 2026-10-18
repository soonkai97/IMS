namespace Ims.Application.Features.Roles;

/// <param name="Id">The public id; the only id a client ever sees.</param>
/// <param name="Code">Stable, never changes; the text a user is saved with.</param>
/// <param name="Permissions">The permission codes the role holds, in catalog order.</param>
/// <param name="UserCount">Live users who hold the role, active or not: what blocks a deletion or a deactivation.</param>
/// <param name="ConcurrencyToken">Changes whenever the role or its permissions change; opaque. Also sent as the ETag header; send it back as If-Match.</param>
public record RoleDto(Guid Id, string Code, string Name, string Description, bool IsSystem, bool IsActive, IReadOnlyList<string> Permissions, int UserCount, string ConcurrencyToken);

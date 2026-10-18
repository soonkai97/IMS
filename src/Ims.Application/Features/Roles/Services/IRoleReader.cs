namespace Ims.Application.Features.Roles;

/// <summary>Reads roles for the API: the permissions in catalog order, the number of users, the concurrency token.</summary>
public interface IRoleReader
{
    Task<IReadOnlyList<RoleDto>> ListAsync(RoleFilter filter, CancellationToken ct);

    /// <summary>The live role with this public id, or null.</summary>
    Task<RoleDto?> FindAsync(Guid publicId, CancellationToken ct);
}

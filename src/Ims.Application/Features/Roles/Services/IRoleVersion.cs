using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>Makes and checks the concurrency token a client sees for a role (<c>concurrencyToken</c>, the ETag, If-Match).</summary>
public interface IRoleVersion
{
    /// <summary>The token of one version of one role; it reveals nothing about the row version it stands for.</summary>
    string Token(Guid publicId, byte[] rowVersion);

    /// <exception cref="ConflictException">A token was sent and it is not the current one of this role: the role changed, or the token is old, altered or from another role.</exception>
    void EnsureCurrent(Role role, string? expectedToken);
}

using Ardalis.Specification;
using Ims.Domain;

namespace Ims.Application;

/// <summary>Live refresh tokens of a user, tracked so they can be revoked when the user's access or password changes.</summary>
public sealed class ActiveRefreshTokensOfUserSpec : Specification<RefreshToken>
{
    public ActiveRefreshTokensOfUserSpec(int userId, DateTime utcNow) =>
        Query.Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > utcNow);
}

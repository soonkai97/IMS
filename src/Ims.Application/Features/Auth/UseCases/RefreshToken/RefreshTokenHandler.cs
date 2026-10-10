using Ims.Domain;
namespace Ims.Application.Features.Auth;

/// <summary>Rotates the refresh token: the presented one is revoked and a new pair is issued.</summary>
public class RefreshTokenHandler(IRepository<RefreshToken> tokensRepo, IReadRepository<User> users, ITokenService tokens, IAuthTokenIssuer issuer, IClock clock)
    : ICommandHandler<RefreshTokenCommand, AuthResult>
{
    public async Task<AuthResult> HandleAsync(RefreshTokenCommand cmd, CancellationToken ct)
    {
        var stored = await tokensRepo.FirstOrDefaultAsync(new RefreshTokenByHashSpec(tokens.Hash(cmd.Body.RefreshToken)), ct);
        var now = clock.UtcNow;
        if (stored is null || !stored.IsActive(now))
            throw new UnauthorizedException(Messages.SessionExpired);

        var user = await users.FirstOrDefaultAsync(new UserWithWarehousesSpec(stored.UserId), ct);
        if (user is null || !user.IsActive || user.IsLockedOut(now))
            throw new UnauthorizedException(Messages.SessionExpired);

        stored.Revoke(now);
        try
        {
            return await issuer.IssueAsync(user, ct);
        }
        catch (ConcurrencyException)
        {
            // Someone redeemed the same refresh token at the same moment: only one request may win; the other must sign in again.
            throw new UnauthorizedException(Messages.SessionExpired);
        }
    }
}

using Ims.Domain;
using Microsoft.Extensions.Logging;
namespace Ims.Application.Features.Auth;

/// <summary>
/// Login by username or email; 5 wrong passwords lock the account temporarily. Failed attempts are counted inside a retried
/// transaction (the user row has a concurrency token), so parallel wrong passwords cannot all read the same counter and dodge the lock.
/// Unknown users leave no audit row (that table is permanent and the text is attacker-chosen): they are logged with a short hash only.
/// </summary>
public class LoginHandler(IRepository<User> users, IPasswordHasher hasher, IAuthTokenIssuer issuer, ITokenService tokens, IUnitOfWork uow,
    IClock clock, IAuditTrail audit, ILogger<LoginHandler> log) : ICommandHandler<LoginCommand, AuthResult>
{
    // Verified against when the account does not exist, so response time does not reveal which usernames are real.
    private static string? _dummyHash;

    private sealed record Attempt(User? User, string? Error, int? LockedForMinutes = null);

    public async Task<AuthResult> HandleAsync(LoginCommand cmd, CancellationToken ct)
    {
        var key = cmd.Body.Username.Trim().ToLowerInvariant();
        var attempt = await uow.ExecuteAsync(() => TryAsync(key, cmd.Body.Password, ct), ct);
        if (attempt.LockedForMinutes is { } minutes) throw new AccountLockedException(minutes);
        if (attempt.Error is not null) throw new UnauthorizedException(attempt.Error);
        return await issuer.IssueAsync(attempt.User!, ct);
    }

    private async Task<Attempt> TryAsync(string key, string password, CancellationToken ct)
    {
        var user = await users.FirstOrDefaultAsync(new UserByLoginSpec(key), ct);

        // One message for unknown user, inactive user and wrong password: no account enumeration.
        if (user is null || !user.IsActive)
        {
            _dummyHash ??= hasher.Hash(Guid.NewGuid().ToString("N"));
            hasher.Verify(_dummyHash, password);
            log.LogWarning("Login failed for an unknown or inactive account (key hash {KeyHash})", tokens.Hash(key)[..12]);
            return new Attempt(null, Messages.InvalidCredentials);
        }

        var now = clock.UtcNow;
        var passwordOk = hasher.Verify(user.PasswordHash, password);
        if (user.IsLockedOut(now))
        {
            audit.Record(AuditActions.LoginLocked, AuditEntities.User, user.PublicId.ToString(), actor: user.Username);
            await uow.SaveChangesAsync(ct);
            // The lock is only disclosed to someone who knows the password; anyone else sees the generic message.
            return passwordOk
                ? new Attempt(null, Messages.AccountLocked, LockedForMinutes: (int)Math.Max(1, Math.Ceiling((user.LockoutUntil!.Value - now).TotalMinutes)))
                : new Attempt(null, Messages.InvalidCredentials);
        }

        if (!passwordOk)
        {
            user.RegisterFailedLogin(now);
            audit.Record(user.LockoutUntil is null ? AuditActions.LoginFailed : AuditActions.LoginLockout, AuditEntities.User, user.PublicId.ToString(), actor: user.Username);
            await uow.SaveChangesAsync(ct);
            return new Attempt(null, Messages.InvalidCredentials);
        }

        user.RegisterSuccessfulLogin();
        audit.Record(AuditActions.LoginSuccess, AuditEntities.User, user.PublicId.ToString(), actor: user.Username);
        await uow.SaveChangesAsync(ct);
        return new Attempt(user, null);
    }
}

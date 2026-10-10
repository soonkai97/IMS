namespace Ims.Application;

/// <summary>
/// Sign-in refused because the account is locked after too many failed attempts (UX E7). Only raised when the caller gave the right password,
/// so the lock is never disclosed to someone who is guessing.
/// </summary>
public class AccountLockedException(int retryAfterMinutes) : UnauthorizedException(Ims.Domain.Messages.AccountLockedFor(retryAfterMinutes))
{
    public int RetryAfterMinutes { get; } = retryAfterMinutes;
}

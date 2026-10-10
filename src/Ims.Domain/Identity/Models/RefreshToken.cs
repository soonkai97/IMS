namespace Ims.Domain;

public class RefreshToken : Entity
{
    private RefreshToken() { }

    public int UserId { get; private set; }
    public string TokenHash { get; private set; } = "";
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }


    public static RefreshToken Issue(int userId, string tokenHash, DateTime now) =>
        new() { UserId = userId, TokenHash = tokenHash, ExpiresAt = now.AddDays(Limits.RefreshTokenDays) };

    public bool IsActive(DateTime utcNow) => RevokedAt is null && ExpiresAt > utcNow;

    public void Revoke(DateTime utcNow) => RevokedAt ??= utcNow;
}

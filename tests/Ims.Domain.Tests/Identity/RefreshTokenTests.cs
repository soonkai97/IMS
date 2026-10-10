using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Identity;

public class RefreshTokenTests
{
    [Fact]
    public void Issue_ExpiresAfterConfiguredDays()
    {
        var token = RefreshToken.Issue(3, "hash", Now);

        Assert.Equal(3, token.UserId);
        Assert.Equal("hash", token.TokenHash);
        Assert.Equal(Now.AddDays(Limits.RefreshTokenDays), token.ExpiresAt);
    }

    [Fact]
    public void IsActive_UntilExpiry()
    {
        var token = RefreshToken.Issue(3, "hash", Now);

        Assert.True(token.IsActive(Now));
        Assert.True(token.IsActive(token.ExpiresAt.AddTicks(-1)));
        Assert.False(token.IsActive(token.ExpiresAt));
    }

    [Fact]
    public void Revoke_DeactivatesTheTokenAndKeepsTheFirstRevocationTime()
    {
        var token = RefreshToken.Issue(3, "hash", Now);

        token.Revoke(Now.AddHours(1));
        token.Revoke(Now.AddHours(2));

        Assert.False(token.IsActive(Now.AddHours(3)));
        Assert.Equal(Now.AddHours(1), token.RevokedAt);
    }
}

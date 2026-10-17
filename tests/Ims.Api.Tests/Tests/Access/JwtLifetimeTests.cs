using System.ComponentModel.DataAnnotations;
using Ims.Api.Tests.Harness;
using Ims.Domain;
using Ims.Infrastructure.Security;

namespace Ims.Api.Tests.Tests.Access;

/// <summary><c>Jwt:AccessMinutes</c> is the revocation window of every right, so the host refuses to start with a value outside 1 to 60 minutes.</summary>
[Collection(ApiCollection.Name)]
public class JwtLifetimeTests(ApiFixture fixture)
{
    private static bool IsValid(int minutes) =>
        Validator.TryValidateObject(new JwtOptions { Key = new string('k', Limits.JwtKeyMinLength), Issuer = "i", Audience = "a", AccessMinutes = minutes },
            new ValidationContext(new JwtOptions()), null, validateAllProperties: true);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(30, true)] // the load-test setting
    [InlineData(60, true)]
    [InlineData(61, false)]
    [InlineData(1440, false)]
    public void Only_one_to_sixty_minutes_are_valid(int minutes, bool valid) => Assert.Equal(valid, IsValid(minutes));

    [Theory]
    [InlineData("61")]
    [InlineData("0")]
    public void The_host_does_not_start_with_an_access_token_lifetime_out_of_range(string minutes)
    {
        using var host = fixture.CreateHost(b => b.UseSetting("Jwt:AccessMinutes", minutes));

        var failure = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        Assert.Contains("AccessMinutes", failure.ToString(), StringComparison.Ordinal);
    }
}

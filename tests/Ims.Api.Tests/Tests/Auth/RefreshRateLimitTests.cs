using System.Net;
using Ims.Api.Tests.Harness;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Auth;

/// <summary>
/// Sign-in keeps its strict per-address limit against password guessing; token refresh has its own, much larger one, so an office behind
/// one address does not lock its users out when every access token expires (or after a deploy) at about the same time.
/// </summary>
[Collection(ApiCollection.Name)]
public class RefreshRateLimitTests(ApiFixture fixture)
{
    private const string NoSuchToken = "not-a-real-refresh-token";

    [Fact]
    public async Task Refreshes_beyond_the_sign_in_limit_from_one_address_are_not_limited()
    {
        var client = fixture.Anonymous();
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < Limits.LoginRateLimitPerMinute + 15; attempt++)
            statuses.Add((await client.Auth.RefreshAsync(NoSuchToken)).Status);

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.Unauthorized, status)); // refused for the token, never for the rate
    }

    [Fact]
    public async Task Exhausting_the_sign_in_limit_does_not_block_refresh_from_the_same_address()
    {
        var client = fixture.Anonymous();
        var unknownUser = "nobody-" + Guid.NewGuid().ToString("N");
        for (var attempt = 0; attempt < Limits.LoginRateLimitPerMinute; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.Auth.LoginAsync(unknownUser, "wrong")).Status);

        var login = await client.Auth.LoginAsync(unknownUser, "wrong");
        var refresh = await client.Auth.RefreshAsync(NoSuchToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, login.Status); // sign-in is still limited at the old threshold
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.Status);
    }

    [Fact]
    public async Task Refresh_has_a_ceiling_and_a_limited_answer_says_how_long_to_wait()
    {
        var client = fixture.Anonymous();
        ApiResponse? limited = null;
        var served = 0;

        for (var attempt = 0; attempt <= Limits.RefreshRateLimitPerMinute && limited is null; attempt++)
        {
            var response = await client.Auth.RefreshAsync(NoSuchToken);
            if (response.Status == HttpStatusCode.TooManyRequests) limited = response;
            else served++;
        }

        Assert.NotNull(limited);
        Assert.Equal(Limits.RefreshRateLimitPerMinute, served);
        Assert.InRange(int.Parse(limited.Header("Retry-After")!), 1, 60);
    }
}

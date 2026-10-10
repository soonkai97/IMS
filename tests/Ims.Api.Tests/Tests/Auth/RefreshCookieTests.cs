using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Auth;
using Ims.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Auth;

/// <summary>SEC-34: the refresh token travels in an HttpOnly cookie that scripts cannot read, with a CSRF defence for the cookie-authenticated calls.</summary>
[Collection(ApiCollection.Name)]
public class RefreshCookieTests(ApiFixture fixture)
{
    private static string CookieName => AuthClient.CookieName;
    private readonly AccountFactory _accounts = new(fixture);

    private async Task<(ApiResponse Login, TestAccount Account)> LoginAsync()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        return (await fixture.Anonymous().Auth.LoginAsync(account.Username, account.Password), account);
    }

    // ---------- issuing ----------

    [Fact]
    public async Task Login_sets_an_HttpOnly_Secure_SameSite_Strict_cookie_scoped_to_the_auth_path()
    {
        var (login, _) = await LoginAsync();

        login.ShouldBeOk();
        var cookie = SetCookie.Find(login, CookieName);
        Assert.NotNull(cookie);
        Assert.StartsWith("__Secure-", cookie.Name);
        Assert.False(string.IsNullOrEmpty(cookie.Value));
        Assert.True(cookie.Has("httponly"));
        Assert.True(cookie.Has("secure"));
        Assert.Equal("strict", cookie.Attribute("samesite"), ignoreCase: true);
        Assert.Equal("/api/auth", cookie.Attribute("path"));
        Assert.False(cookie.Has("domain"), "A Domain attribute would widen the cookie to subdomains.");
        Assert.Equal(TimeSpan.FromDays(Limits.RefreshTokenDays).TotalSeconds, double.Parse(cookie.Attribute("max-age")!));
    }

    [Fact]
    public async Task The_cookie_is_Secure_even_though_the_API_itself_is_reached_over_plain_http()
    {
        // The test server speaks http, like an API behind a TLS-terminating proxy; only Development may drop the flag.
        var (login, _) = await LoginAsync();

        Assert.True(SetCookie.Find(login, CookieName)!.Has("secure"));
    }

    [Fact]
    public async Task While_the_deprecated_body_delivery_is_on_the_body_still_carries_the_same_refresh_token()
    {
        var (login, _) = await LoginAsync();

        var body = login.Data<AuthResult>();

        Assert.False(string.IsNullOrEmpty(body.RefreshToken));
        Assert.Equal(body.RefreshToken, SetCookie.Find(login, CookieName)!.Value);
    }

    [Fact]
    public void The_code_default_keeps_the_refresh_token_out_of_the_response_body()
    {
        Assert.False(new Ims.Api.Features.Auth.AuthOptions().ReturnRefreshTokenInBody);
    }

    [Fact]
    public void The_shipped_appsettings_keep_the_refresh_token_out_of_the_response_body()
    {
        // The test host forces the deprecated body delivery on for the session tests; the file an operator deploys must say off (EA-05).
        var contentRoot = fixture.Host.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
        var shipped = new ConfigurationBuilder().AddJsonFile(Path.Combine(contentRoot, "appsettings.json")).Build();

        Assert.False(shipped.GetValue<bool?>("Auth:ReturnRefreshTokenInBody"));
    }

    [Fact]
    public async Task With_ReturnRefreshTokenInBody_off_the_token_is_only_in_the_cookie_and_the_cookie_flow_works()
    {
        using var host = fixture.CreateHost(b => b.UseSetting("Auth:ReturnRefreshTokenInBody", "false"));
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var client = ApiFixture.CreateClient(host);

        var login = await client.Auth.LoginAsync(account.Username, account.Password);

        login.ShouldBeOk();
        Assert.Null(login.Json().RootElement.GetProperty("refreshToken").GetString());
        var cookie = SetCookie.Find(login, CookieName)!;
        Assert.False(string.IsNullOrEmpty(cookie.Value));

        var refreshed = await client.Auth.RefreshWithCookieAsync(cookie.Value);
        refreshed.ShouldBeOk();
        Assert.Null(refreshed.Json().RootElement.GetProperty("refreshToken").GetString());
        Assert.NotEqual(cookie.Value, SetCookie.Find(refreshed, CookieName)!.Value);
    }

    // ---------- rotation, reuse, logout over the cookie ----------

    [Fact]
    public async Task Refresh_with_the_cookie_rotates_it_and_issues_a_working_access_token()
    {
        var (login, account) = await LoginAsync();
        var first = SetCookie.Find(login, CookieName)!;

        var refreshed = await fixture.Anonymous().Auth.RefreshWithCookieAsync(first.Value);

        var result = refreshed.Data<AuthResult>();
        var second = SetCookie.Find(refreshed, CookieName)!;
        Assert.NotEqual(first.Value, second.Value);
        Assert.True(second.Has("httponly") && second.Has("secure"));
        var me = await fixture.CreateClient(result.AccessToken).Auth.MeAsync();
        Assert.Equal(account.Username, me.Data<UserInfo>().Username);
    }

    [Fact]
    public async Task A_rotated_cookie_token_replayed_is_refused_and_the_browser_is_told_to_drop_the_cookie()
    {
        var (login, _) = await LoginAsync();
        var first = SetCookie.Find(login, CookieName)!;
        var second = SetCookie.Find((await fixture.Anonymous().Auth.RefreshWithCookieAsync(first.Value)).ShouldBeOk(), CookieName)!;

        var replay = await fixture.Anonymous().Auth.RefreshWithCookieAsync(first.Value);

        replay.ShouldBeProblem(HttpStatusCode.Unauthorized);
        var cleared = SetCookie.Find(replay, CookieName);
        Assert.NotNull(cleared);
        Assert.True(cleared.IsDeletion, "A dead refresh token must not stay in the browser.");
        Assert.Equal("/api/auth", cleared.Attribute("path"));
        // The live token is untouched by the failed replay (the pre-existing behaviour: no token-family revocation).
        (await fixture.Anonymous().Auth.RefreshWithCookieAsync(second.Value)).ShouldBeOk();
    }

    [Fact]
    public async Task Refresh_with_the_body_token_still_works_without_the_csrf_header_and_also_sets_the_cookie()
    {
        var (login, _) = await LoginAsync();
        var token = login.Data<AuthResult>().RefreshToken;

        var refreshed = await fixture.Anonymous().Auth.RefreshAsync(token);

        refreshed.ShouldBeOk();
        Assert.NotNull(SetCookie.Find(refreshed, CookieName));
        Assert.NotEqual(token, refreshed.Data<AuthResult>().RefreshToken);
    }

    [Fact]
    public async Task Logout_with_the_cookie_revokes_the_token_and_clears_the_cookie()
    {
        var (login, _) = await LoginAsync();
        var cookie = SetCookie.Find(login, CookieName)!;

        var logout = await fixture.Anonymous().Auth.LogoutWithCookieAsync(cookie.Value);

        logout.ShouldBe(HttpStatusCode.NoContent);
        Assert.True(SetCookie.Find(logout, CookieName)!.IsDeletion);
        (await fixture.Anonymous().Auth.RefreshWithCookieAsync(cookie.Value)).ShouldBeProblem(HttpStatusCode.Unauthorized);
        (await fixture.Anonymous().Auth.RefreshAsync(login.Data<AuthResult>().RefreshToken)).ShouldBeProblem(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_with_the_body_token_still_works_and_clears_the_cookie_too()
    {
        var (login, _) = await LoginAsync();

        var logout = await fixture.Anonymous().Auth.LogoutAsync(login.Data<AuthResult>().RefreshToken);

        logout.ShouldBe(HttpStatusCode.NoContent);
        Assert.True(SetCookie.Find(logout, CookieName)!.IsDeletion);
    }

    [Fact]
    public async Task Without_any_token_or_cookie_refresh_is_401_and_logout_is_quiet()
    {
        var client = fixture.Anonymous();

        (await client.Auth.RefreshWithCookieAsync(null)).ShouldBeProblem(HttpStatusCode.Unauthorized);
        (await client.Auth.RefreshAsync(null)).ShouldBeProblem(HttpStatusCode.Unauthorized);        // body {}
        (await client.Auth.RefreshAsync("")).ShouldBeProblem(HttpStatusCode.BadRequest);            // an explicitly empty token is a validation error
        (await client.Auth.LogoutWithCookieAsync(null)).ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_browser_may_post_with_no_body_and_no_content_type_at_all()
    {
        var (login, _) = await LoginAsync();
        var cookie = SetCookie.Find(login, CookieName)!.Value;

        var refresh = await fixture.Anonymous().SendAsync(HttpMethod.Post, "/api/auth/refresh", customize: r => AddBrowserHeaders(r, cookie));
        var logout = await fixture.Anonymous().SendAsync(HttpMethod.Post, "/api/auth/logout", customize: r => AddBrowserHeaders(r, SetCookie.Find(refresh.ShouldBeOk(), CookieName)!.Value));

        logout.ShouldBe(HttpStatusCode.NoContent);
    }

    private static void AddBrowserHeaders(HttpRequestMessage request, string cookie)
    {
        request.Headers.Add("Cookie", $"{CookieName}={Uri.EscapeDataString(cookie)}");
        request.Headers.Add("Origin", BrowserRequest.AllowedOrigin);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
    }

    [Fact]
    public async Task A_cookie_without_the_secure_prefix_is_not_accepted()
    {
        var (login, _) = await LoginAsync();
        var token = SetCookie.Find(login, CookieName)!.Value;

        var response = await fixture.Anonymous().SendAsync(HttpMethod.Post, "/api/auth/refresh", customize: r =>
        {
            r.Content = new StringContent("", System.Text.Encoding.UTF8, "application/json");
            r.Headers.Add("Cookie", $"{CookieName.Replace("__Secure-", "")}={Uri.EscapeDataString(token)}");
            r.Headers.Add("Origin", BrowserRequest.AllowedOrigin);
            r.Headers.Add("X-Requested-With", "XMLHttpRequest");
        });

        response.ShouldBeProblem(HttpStatusCode.Unauthorized); // the look-alike cookie is ignored: no token presented
    }

    // ---------- CSRF defence ----------

    [Fact]
    public async Task A_cookie_refresh_without_the_custom_header_is_403_and_does_not_burn_the_token()
    {
        var (login, _) = await LoginAsync();
        var cookie = SetCookie.Find(login, CookieName)!.Value;

        var forged = await fixture.Anonymous().Auth.RefreshWithCookieAsync(cookie, BrowserRequest.Trusted.WithRequestedWith(null));

        forged.ShouldBeProblem(HttpStatusCode.Forbidden);
        Assert.Null(SetCookie.Find(forged, CookieName));
        (await fixture.Anonymous().Auth.RefreshWithCookieAsync(cookie)).ShouldBeOk();
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://localhost:4200.evil.example")]
    [InlineData("http://localhost:4201")]
    [InlineData("null")]
    public async Task A_cookie_refresh_from_a_foreign_origin_is_403(string origin)
    {
        var (login, _) = await LoginAsync();
        var cookie = SetCookie.Find(login, CookieName)!.Value;

        var forged = await fixture.Anonymous().Auth.RefreshWithCookieAsync(cookie, BrowserRequest.Trusted.WithOrigin(origin));

        forged.ShouldBeProblem(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_cookie_refresh_with_neither_origin_nor_referer_is_403()
    {
        var (login, _) = await LoginAsync();

        var forged = await fixture.Anonymous().Auth.RefreshWithCookieAsync(SetCookie.Find(login, CookieName)!.Value, BrowserRequest.Trusted.WithOrigin(null));

        forged.ShouldBeProblem(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_referer_is_used_when_there_is_no_origin_and_must_also_be_an_allowed_origin()
    {
        var (login, _) = await LoginAsync();
        var cookie = SetCookie.Find(login, CookieName)!.Value;
        var noOrigin = BrowserRequest.Trusted.WithOrigin(null);

        (await fixture.Anonymous().Auth.RefreshWithCookieAsync(cookie, noOrigin.WithReferer("https://evil.example/page"))).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await fixture.Anonymous().Auth.RefreshWithCookieAsync(cookie, noOrigin.WithReferer("not a url"))).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await fixture.Anonymous().Auth.RefreshWithCookieAsync(cookie, noOrigin.WithReferer("http://localhost:4200/app/dashboard?x=1"))).ShouldBeOk();
    }

    [Fact]
    public async Task A_cookie_logout_from_a_foreign_origin_is_403_and_leaves_the_session_alive()
    {
        var (login, _) = await LoginAsync();
        var cookie = SetCookie.Find(login, CookieName)!.Value;

        var forged = await fixture.Anonymous().Auth.LogoutWithCookieAsync(cookie, BrowserRequest.Trusted.WithOrigin("https://evil.example"));

        forged.ShouldBeProblem(HttpStatusCode.Forbidden);
        (await fixture.Anonymous().Auth.RefreshWithCookieAsync(cookie)).ShouldBeOk();
    }

    [Fact]
    public async Task Cors_allows_credentials_for_the_configured_origin_only()
    {
        var allowed = await fixture.Anonymous().SendAsync(HttpMethod.Options, "/api/auth/refresh", customize: r =>
        {
            r.Headers.Add("Origin", BrowserRequest.AllowedOrigin);
            r.Headers.Add("Access-Control-Request-Method", "POST");
            r.Headers.Add("Access-Control-Request-Headers", "content-type,x-requested-with");
        });
        var foreign = await fixture.Anonymous().SendAsync(HttpMethod.Options, "/api/auth/refresh", customize: r =>
        {
            r.Headers.Add("Origin", "https://evil.example");
            r.Headers.Add("Access-Control-Request-Method", "POST");
        });

        Assert.Equal("true", allowed.Header("Access-Control-Allow-Credentials"));
        Assert.Equal(BrowserRequest.AllowedOrigin, allowed.Header("Access-Control-Allow-Origin"));
        Assert.Contains("x-requested-with", allowed.Header("Access-Control-Allow-Headers"), StringComparison.OrdinalIgnoreCase);
        Assert.Null(foreign.Header("Access-Control-Allow-Origin"));
        Assert.Null(foreign.Header("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public void A_wildcard_cors_origin_stops_the_host_from_starting()
    {
        using var host = fixture.CreateHost(b => b.UseSetting("Cors:Origins", "*"));

        var failure = Assert.ThrowsAny<Exception>(() => _ = host.Services);

        Assert.Contains("Cors:Origins", failure.ToString());
    }
}

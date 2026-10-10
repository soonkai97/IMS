using Ims.Api.Features.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Ims.Api.Tests.Tests.Auth;

/// <summary>
/// When the cookie may drop the Secure flag: only a Development server reached over plain http. Everywhere else it is always Secure,
/// whatever scheme the API sees (a TLS proxy in front makes the API see http while the browser is on https).
/// </summary>
public class RefreshTokenCookieEnvironmentTests
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static string WrittenCookie(string environment, string scheme)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        new RefreshTokenCookie(new FakeEnvironment(environment)).Write(context, "token-value");
        return context.Response.Headers.SetCookie.ToString();
    }

    [Fact]
    public void Development_over_plain_http_gets_the_plain_name_and_no_secure_flag_but_stays_HttpOnly_and_Strict()
    {
        var header = WrittenCookie(Environments.Development, "http");

        Assert.StartsWith(AuthCookie.RefreshName(secure: false) + "=", header);
        Assert.DoesNotContain("__Secure-", header);
        Assert.DoesNotContain("secure;", header + ";", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", header, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Development", "https")]
    [InlineData("Production", "https")]
    [InlineData("Production", "http")] // behind a proxy that terminates TLS and was not told so
    [InlineData("Staging", "http")]
    [InlineData("Testing", "http")]
    public void Every_other_case_is_Secure_with_the_prefixed_name(string environment, string scheme)
    {
        var header = WrittenCookie(environment, scheme);

        Assert.StartsWith(AuthCookie.RefreshName(secure: true) + "=", header);
        Assert.Contains("secure", header.Replace("__Secure-", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_cookie_name_follows_the_product_name_and_carries_the_secure_prefix_when_secure()
    {
        Assert.Equal("__Secure-ims-refresh", AuthCookie.RefreshName(secure: true));
        Assert.Equal("ims-refresh", AuthCookie.RefreshName(secure: false));
    }

    [Fact]
    public void Read_only_looks_at_the_name_that_fits_the_security_of_the_request()
    {
        var production = new DefaultHttpContext();
        production.Request.Headers.Cookie = $"{AuthCookie.RefreshName(secure: false)}=planted; {AuthCookie.RefreshName(secure: true)}=real";
        var cookie = new RefreshTokenCookie(new FakeEnvironment(Environments.Production));

        Assert.Equal("real", cookie.Read(production));

        production.Request.Headers.Cookie = $"{AuthCookie.RefreshName(secure: false)}=planted";
        Assert.Null(cookie.Read(production));
    }
}

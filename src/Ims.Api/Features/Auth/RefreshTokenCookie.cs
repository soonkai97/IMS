using Ims.Domain;

namespace Ims.Api.Features.Auth;

/// <summary>
/// <para>
/// Secure is on everywhere except a Development server reached over plain HTTP. It does not depend on what the API sees: behind a TLS
/// proxy the API may be reached over HTTP while the browser is on HTTPS, and a cookie without Secure there would travel in clear on
/// any later http:// request to the host. Only a developer on http://localhost gets the plain name and no Secure flag.
/// </para>
/// <para>
/// Reading uses the name that matches the same decision, so a look-alike cookie without the <c>__Secure-</c> prefix (which a
/// network attacker or a sibling subdomain can plant) is never accepted in production.
/// </para>
/// </summary>
public sealed class RefreshTokenCookie(IHostEnvironment environment) : IRefreshTokenCookie
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(Limits.RefreshTokenDays);

    public string? Read(HttpContext context) =>
        context.Request.Cookies.TryGetValue(AuthCookie.RefreshName(IsSecure(context)), out var value) && !string.IsNullOrEmpty(value) ? value : null;

    public void Write(HttpContext context, string refreshToken)
    {
        var options = Options(context);
        options.MaxAge = Lifetime;
        context.Response.Cookies.Append(AuthCookie.RefreshName(IsSecure(context)), refreshToken, options);
    }

    public void Clear(HttpContext context) =>
        context.Response.Cookies.Delete(AuthCookie.RefreshName(IsSecure(context)), Options(context));

    private bool IsSecure(HttpContext context) => !environment.IsDevelopment() || context.Request.IsHttps;

    private CookieOptions Options(HttpContext context) => new()
    {
        HttpOnly = true,
        Secure = IsSecure(context),
        SameSite = SameSiteMode.Strict,
        Path = context.Request.PathBase + AuthCookie.Path,
        IsEssential = true
    };
}

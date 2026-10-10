namespace Ims.Api.Features.Auth;

/// <summary>
/// Cross-site request forgery defence for requests whose credential is a cookie (the browser attaches it by itself, so the request
/// proves nothing about who started it). Bearer-token and body-token requests need no such check.
/// </summary>
public interface ICsrfGuard
{
    /// <summary>
    /// Passes only if the request carries the custom header <see cref="AuthCookie.CsrfHeaderName"/> and its Origin (or, if absent, its
    /// Referer) is one of the configured CORS origins.
    /// </summary>
    /// <exception cref="Ims.Application.ForbiddenException">The request does not look like it came from our own web application.</exception>
    void EnsureTrusted(HttpContext context);
}

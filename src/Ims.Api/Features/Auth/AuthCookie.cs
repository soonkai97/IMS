namespace Ims.Api.Features.Auth;

/// <summary>Names and values of the refresh-token cookie and of the CSRF header that must accompany it.</summary>
public static class AuthCookie
{
    /// <summary>
    /// The cookie is only sent to the auth endpoints, so its Path is not "/". That rules out the <c>__Host-</c> prefix (which demands
    /// Path=/); <c>__Secure-</c> still makes browsers refuse the cookie unless it was set over HTTPS with the Secure flag.
    /// </summary>
    public const string SecurePrefix = "__Secure-";

    /// <summary>Path scope: the cookie travels with login, refresh, logout and nothing else.</summary>
    public const string Path = "/" + ApiRoutes.Auth;

    /// <summary>
    /// Header a cookie-authenticated request must carry. A cross-site form or image cannot add it, and a cross-origin script can only add
    /// it after a CORS preflight that the allowed origins alone pass.
    /// </summary>
    public const string CsrfHeaderName = HeaderNames.RequestedWith;
    public const string CsrfHeaderValue = "XMLHttpRequest";

    /// <summary>"__Secure-ims-refresh" (HTTPS) or "ims-refresh" (plain HTTP, Development only); the product part follows the assembly name.</summary>
    public static string RefreshName(bool secure) => (secure ? SecurePrefix : "") + AppIdentity.Slug + "-refresh";
}

namespace Ims.Api.Features.Auth;

/// <summary>Settings of the sign-in transport, bound from the <c>Auth</c> configuration section.</summary>
public sealed class AuthOptions
{
    /// <summary>
    /// Whether login and refresh also return the refresh token in the JSON body (deprecated) in addition to the HttpOnly cookie.
    /// Off by default: the web app uses the cookie, so no script (and no proxy log of a response body) ever sees the 7-day token.
    /// Set <c>Auth:ReturnRefreshTokenInBody=true</c> only for an old client that still reads the token from JavaScript.
    /// </summary>
    public bool ReturnRefreshTokenInBody { get; set; }
}

namespace Ims.Api.Features.Auth;

/// <summary>Settings of the sign-in transport, bound from the <c>Auth</c> configuration section.</summary>
public sealed class AuthOptions
{
    /// <summary>
    /// Whether login and refresh still return the refresh token in the JSON body (deprecated) in addition to the HttpOnly cookie.
    /// True keeps clients that read the token from JavaScript working; set <c>Auth:ReturnRefreshTokenInBody=false</c> once the web app
    /// uses the cookie, so no script can ever read the token.
    /// </summary>
    public bool ReturnRefreshTokenInBody { get; set; } = true;
}

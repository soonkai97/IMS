namespace Ims.Api.Features.Auth;

/// <summary>OpenAPI descriptions of the sign-in endpoints (the contract for the web client).</summary>
public static class AuthDocs
{
    public const string Login =
        "Signs in and returns the access token. The refresh token is set as an HttpOnly, Secure, SameSite=Strict cookie scoped to /api/auth. " +
        "DEPRECATED: while Auth:ReturnRefreshTokenInBody is true the refresh token is also returned in the JSON body; clients must stop reading it.";

    public const string Refresh =
        "Rotates the refresh token and returns a new access token. The refresh token is read from the HttpOnly cookie; the browser must send " +
        "credentials, the header X-Requested-With: XMLHttpRequest, and an Origin that is in Cors:Origins (otherwise 403). " +
        "DEPRECATED: a refreshToken in the request body is still accepted and takes precedence; so is the refresh token in the response body " +
        "while Auth:ReturnRefreshTokenInBody is true.";

    public const string Logout =
        "Revokes the refresh token (from the cookie, or DEPRECATED from the request body) and clears the cookie. Always 204 for an unknown or " +
        "already revoked token. A cookie-authenticated call needs the same X-Requested-With header and allowed Origin as refresh.";

    public const string RefreshTokenProperty =
        "DEPRECATED. The refresh token now travels in an HttpOnly cookie; this property is only filled while Auth:ReturnRefreshTokenInBody is true.";
}

namespace Ims.Api.Features.Auth;

/// <summary>Reads, writes and clears the HttpOnly cookie that carries the refresh token.</summary>
public interface IRefreshTokenCookie
{
    /// <returns>The cookie's value, or null when the request has none.</returns>
    string? Read(HttpContext context);

    /// <summary>Sets the cookie: HttpOnly, Secure, SameSite=Strict, scoped to the auth path, living as long as the refresh token.</summary>
    void Write(HttpContext context, string refreshToken);

    /// <summary>Tells the browser to drop the cookie (same attributes as <see cref="Write"/>, expired).</summary>
    void Clear(HttpContext context);
}

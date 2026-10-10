namespace Ims.Application.Features.Auth;

/// <summary>
/// The result of a login or refresh. <paramref name="RefreshToken"/> is null when the API is configured to deliver the refresh token
/// only in its HttpOnly cookie (Auth:ReturnRefreshTokenInBody=false).
/// </summary>
public record AuthResult(string AccessToken, DateTime ExpiresAt, string? RefreshToken, UserInfo User);

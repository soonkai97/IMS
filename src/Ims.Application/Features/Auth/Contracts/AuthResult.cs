namespace Ims.Application.Features.Auth;

public record AuthResult(string AccessToken, DateTime ExpiresAt, string RefreshToken, UserInfo User);

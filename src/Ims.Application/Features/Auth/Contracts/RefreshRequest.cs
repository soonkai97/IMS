namespace Ims.Application.Features.Auth;

/// <summary>The refresh token to redeem or revoke. Optional on the wire: when absent, the API reads it from the HttpOnly cookie.</summary>
public record RefreshRequest(string? RefreshToken);

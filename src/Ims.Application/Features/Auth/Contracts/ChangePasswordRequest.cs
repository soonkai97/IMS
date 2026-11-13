namespace Ims.Application.Features.Auth;

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

namespace Ims.Application.Features.Auth;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

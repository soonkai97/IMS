namespace Ims.Application.Features.Auth;

public record ChangePasswordCommand(ChangePasswordRequest Body) : ICommand<Unit>;

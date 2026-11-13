namespace Ims.Application.Features.Auth;

public sealed record ChangePasswordCommand(ChangePasswordRequest Body) : ICommand<Unit>;

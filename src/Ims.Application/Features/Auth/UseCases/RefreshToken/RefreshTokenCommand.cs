namespace Ims.Application.Features.Auth;

public sealed record RefreshTokenCommand(RefreshRequest Body) : ICommand<AuthResult>;

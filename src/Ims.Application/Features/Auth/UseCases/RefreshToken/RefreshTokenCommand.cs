namespace Ims.Application.Features.Auth;

public record RefreshTokenCommand(RefreshRequest Body) : ICommand<AuthResult>;

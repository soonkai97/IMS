namespace Ims.Application.Features.Auth;

public record LoginCommand(LoginRequest Body) : ICommand<AuthResult>;

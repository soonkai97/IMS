namespace Ims.Application.Features.Auth;

public sealed record LoginCommand(LoginRequest Body) : ICommand<AuthResult>;

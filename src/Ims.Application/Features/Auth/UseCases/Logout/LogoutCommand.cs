namespace Ims.Application.Features.Auth;

public sealed record LogoutCommand(RefreshRequest Body) : ICommand<Unit>;

namespace Ims.Application.Features.Auth;

public record LogoutCommand(RefreshRequest Body) : ICommand<Unit>;

namespace Ims.Application.Features.Roles;

public sealed record DeleteRoleCommand(Guid Id) : ICommand<Unit>;

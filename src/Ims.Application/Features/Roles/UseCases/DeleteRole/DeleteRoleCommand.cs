namespace Ims.Application.Features.Roles;

public record DeleteRoleCommand(Guid Id) : ICommand<Unit>;

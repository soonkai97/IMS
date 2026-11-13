namespace Ims.Application.Features.Users;

public sealed record UpdateUserCommand(Guid Id, UserSaveRequest Data) : ICommand<UserDto>;

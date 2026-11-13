namespace Ims.Application.Features.Users;

public sealed record CreateUserCommand(UserSaveRequest Data) : ICommand<UserDto>;

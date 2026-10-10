namespace Ims.Application.Features.Users;

public record CreateUserCommand(UserSaveRequest Data) : ICommand<UserDto>;

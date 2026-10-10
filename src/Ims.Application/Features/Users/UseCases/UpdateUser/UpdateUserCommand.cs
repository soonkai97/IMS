namespace Ims.Application.Features.Users;

public record UpdateUserCommand(int Id, UserSaveRequest Data) : ICommand<UserDto>;

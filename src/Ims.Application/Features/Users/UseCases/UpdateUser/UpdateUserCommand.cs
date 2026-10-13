namespace Ims.Application.Features.Users;

public record UpdateUserCommand(Guid Id, UserSaveRequest Data) : ICommand<UserDto>;

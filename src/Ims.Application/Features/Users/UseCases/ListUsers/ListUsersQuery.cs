namespace Ims.Application.Features.Users;

public record ListUsersQuery(UserFilter Filter) : IQuery<PagedResult<UserDto>>;

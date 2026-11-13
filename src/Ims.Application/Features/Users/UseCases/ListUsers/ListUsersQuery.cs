namespace Ims.Application.Features.Users;

public sealed record ListUsersQuery(UserFilter Filter) : IQuery<PagedResult<UserDto>>;

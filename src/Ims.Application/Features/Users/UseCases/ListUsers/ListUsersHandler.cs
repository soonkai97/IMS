using Ims.Domain;
namespace Ims.Application.Features.Users;

public class ListUsersHandler(IReadRepository<User> users) : IQueryHandler<ListUsersQuery, PagedResult<UserDto>>
{
    public async Task<PagedResult<UserDto>> HandleAsync(ListUsersQuery q, CancellationToken ct)
    {
        var total = await users.CountAsync(new UsersCountSpec(q.Filter), ct);
        var items = await users.ListAsync(new UsersListSpec(q.Filter), ct);
        var (page, size) = Paging.Normalize(q.Filter.Page, q.Filter.PageSize);
        return new PagedResult<UserDto>(items, total, page, size);
    }
}

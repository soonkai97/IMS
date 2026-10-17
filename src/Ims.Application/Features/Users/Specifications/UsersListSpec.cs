using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Users;

/// <summary>One page of users ordered by username.</summary>
public sealed class UsersListSpec : Specification<User, UserDto>
{
    public UsersListSpec(UserFilter f)
    {
        UserFilters.Apply(Query, f);
        var (page, size) = Paging.Normalize(f.Page, f.PageSize);
        Query.OrderBy(u => u.Username).Skip((page - 1) * size).Take(size)
            .Select(u => new UserDto(u.PublicId, u.Username, u.Email, u.DisplayName, u.Role.Code, u.IsActive, u.Warehouses.Select(w => w.Warehouse.PublicId).ToList(), u.AllWarehouses));
    }
}

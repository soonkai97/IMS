using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Users;

public sealed class UsersCountSpec : Specification<User>
{
    public UsersCountSpec(UserFilter f) => UserFilters.Apply(Query, f);
}

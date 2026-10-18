using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>The id of the role a user holds now, from the database (the role claim of the token can be a few minutes old).</summary>
public sealed class RoleIdOfUserSpec : Specification<User, int>
{
    public RoleIdOfUserSpec(int userId) => Query.AsNoTracking().Where(u => u.Id == userId).Select(u => u.RoleId);
}

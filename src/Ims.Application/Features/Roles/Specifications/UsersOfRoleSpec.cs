using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>The live users who hold a role, with their warehouses (read only: the scope check of a role edit).</summary>
public sealed class UsersOfRoleSpec : Specification<User>
{
    public UsersOfRoleSpec(int roleId) => Query.AsNoTracking().Where(u => u.RoleId == roleId).Include(u => u.Warehouses);
}

using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>A live role with this name other than the given one (the database ignores case and trailing spaces, as the unique index does).</summary>
public sealed class RoleNameTakenSpec : Specification<Role>
{
    public RoleNameTakenSpec(string name, int exceptRoleId = 0) => Query.AsNoTracking().Where(r => r.Name == name && r.Id != exceptRoleId);
}

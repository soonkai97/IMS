using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Auth;

/// <summary>A live role with the codes of its live grants, by primary key (a tiny table; the grants are one indexed range).</summary>
public sealed class AccessRoleSpec : Specification<Role, AccessRoleRow>
{
    public AccessRoleSpec(int roleId) =>
        Query.AsNoTracking().Where(r => r.Id == roleId)
            .Select(r => new AccessRoleRow(r.Code, r.Name, r.Grants.Select(g => g.PermissionCode).ToList()));
}

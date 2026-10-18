using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>The live roles (a tiny table: the system roles first, then the custom ones by name) with the codes of their live grants.</summary>
public sealed class RolesListSpec : Specification<Role, RoleRow>
{
    public RolesListSpec(RoleFilter f)
    {
        Query.AsNoTracking();
        if (!f.IncludeInactive) Query.Where(r => r.IsActive);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            Query.Where(r => r.Name.Contains(s) || r.Description.Contains(s));
        }
        Query.OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name)
            .Select(r => new RoleRow(r.Id, r.PublicId, r.Code, r.Name, r.Description, r.IsSystem, r.IsActive, r.RowVersion, r.Grants.Select(g => g.PermissionCode).ToList()));
    }
}

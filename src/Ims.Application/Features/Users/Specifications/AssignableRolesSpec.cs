using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Users;

/// <summary>The live, active roles with their grants (a tiny table), the system roles first then the custom ones by name.</summary>
public sealed class AssignableRolesSpec : Specification<Role>
{
    public AssignableRolesSpec() => Query.AsNoTracking().Where(r => r.IsActive).Include(r => r.Grants).OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name);
}

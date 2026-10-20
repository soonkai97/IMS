using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Modules;

/// <summary>The live roles that hold at least one of the permissions (read only): who gets a module's permissions back when it is turned on.</summary>
public sealed class RolesHoldingPermissionsSpec : Specification<Role>
{
    public RolesHoldingPermissionsSpec(IReadOnlyCollection<string> codes) => Query.Where(r => r.Grants.Any(g => codes.Contains(g.PermissionCode))).AsNoTracking();
}

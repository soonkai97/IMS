using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Users;

/// <summary>
/// The live role with this code, tracked and with its grants, for giving it to a user: the user then points at the very instance the context holds, and the grants are what
/// <see cref="UserContainment"/> compares with the caller's. The database collation ignores case and trailing spaces, so the caller of this spec confirms the code exactly
/// (<see cref="UserContainment.ExactRole"/>).
/// </summary>
public sealed class RoleForAssignmentSpec : Specification<Role>
{
    public RoleForAssignmentSpec(string code) => Query.Where(r => r.Code == code).Include(r => r.Grants);
}

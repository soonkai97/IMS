using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>One live role by its public id, tracked and with its live grants: what a write changes and what <see cref="RoleContainment"/> compares with the caller's rights.</summary>
public sealed class RoleForUpdateSpec : Specification<Role>
{
    public RoleForUpdateSpec(Guid publicId) => Query.Where(r => r.PublicId == publicId).Include(r => r.Grants);
}

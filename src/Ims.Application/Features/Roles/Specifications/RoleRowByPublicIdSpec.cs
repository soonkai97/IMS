using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Roles;

/// <summary>One live role by its public id, read only.</summary>
public sealed class RoleRowByPublicIdSpec : Specification<Role, RoleRow>
{
    public RoleRowByPublicIdSpec(Guid publicId) =>
        Query.AsNoTracking().Where(r => r.PublicId == publicId)
            .Select(r => new RoleRow(r.Id, r.PublicId, r.Code, r.Name, r.Description, r.IsSystem, r.IsActive, r.RowVersion, r.Grants.Select(g => g.PermissionCode).ToList()));
}

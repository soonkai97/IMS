using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Users;

/// <summary>
/// The user a client named by public id, with the warehouses they are assigned to (tracked, for an update). Found whatever the state of their role (see <see cref="UserFilters"/>), so a
/// user whose role was deleted can be moved to a live one.
/// </summary>
public sealed class UserWithWarehousesByPublicIdSpec : Specification<User>
{
    public UserWithWarehousesByPublicIdSpec(Guid publicId) => Query.IgnoreQueryFilters().Where(u => u.PublicId == publicId && !u.RecDeleted).Include(u => u.Warehouses.Where(w => !w.RecDeleted)).Include(u => u.Role).ThenInclude(r => r.Grants);
}

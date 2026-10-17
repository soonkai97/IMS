using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Users;

/// <summary>The user a client named by public id, with the warehouses they are assigned to (tracked, for an update).</summary>
public sealed class UserWithWarehousesByPublicIdSpec : Specification<User>
{
    public UserWithWarehousesByPublicIdSpec(Guid publicId) => Query.Where(u => u.PublicId == publicId).Include(u => u.Warehouses).Include(u => u.Role).ThenInclude(r => r.Grants);
}

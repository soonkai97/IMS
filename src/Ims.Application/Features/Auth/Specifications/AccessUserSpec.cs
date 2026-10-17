using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Auth;

/// <summary>An active user's role id and data scope, by primary key. No row for a missing or inactive user.</summary>
public sealed class AccessUserSpec : Specification<User, AccessUserRow>
{
    public AccessUserSpec(int userId) =>
        Query.AsNoTracking().Where(u => u.Id == userId && u.IsActive)
            .Select(u => new AccessUserRow(u.RoleId, u.AllWarehouses, u.Warehouses.Select(w => w.WarehouseId).ToList()));
}

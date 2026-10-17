using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Users;

internal static class UserMapping
{
    /// <summary>The user's warehouses leave as public ids (one query for the list, see <see cref="IIdMap{T}"/>).</summary>
    public static async Task<UserDto> ToDtoAsync(User u, IIdMap<Warehouse> warehouses, CancellationToken ct)
    {
        var publicIds = await warehouses.ToPublicAsync(u.Warehouses.Select(w => w.WarehouseId), ct);
        return new(u.PublicId, u.Username, u.Email, u.DisplayName, u.Role.Code, u.IsActive, u.Warehouses.Select(w => publicIds[w.WarehouseId]).ToList(), u.AllWarehouses);
    }
}

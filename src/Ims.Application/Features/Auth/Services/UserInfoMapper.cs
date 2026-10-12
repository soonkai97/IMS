using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Auth;

public static class UserInfoMapper
{
    /// <summary>The user's warehouses leave as public ids (one query for the list); the access token keeps the integer ids the data scope uses.</summary>
    public static async Task<UserInfo> ToInfoAsync(User u, IIdMap<Warehouse> warehouses, CancellationToken ct)
    {
        var publicIds = await warehouses.ToPublicAsync(u.Warehouses.Select(w => w.WarehouseId), ct);
        return new(u.Id, u.Username, u.DisplayName, u.Role, Permissions.For(u.Role).ToList(), u.Warehouses.Select(w => publicIds[w.WarehouseId]).ToList());
    }
}

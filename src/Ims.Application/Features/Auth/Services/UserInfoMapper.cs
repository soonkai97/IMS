using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Auth;

public static class UserInfoMapper
{
    public static UserInfo ToInfo(User u) =>
        new(u.Id, u.Username, u.DisplayName, u.Role, Permissions.For(u.Role).ToList(), u.Warehouses.Select(w => w.WarehouseId).ToList());
}

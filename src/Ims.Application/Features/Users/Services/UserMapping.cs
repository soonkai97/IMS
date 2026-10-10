using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Users;

internal static class UserMapping
{
    public static UserDto ToDto(User u) =>
        new(u.Id, u.Username, u.Email, u.DisplayName, u.Role, u.IsActive, u.Warehouses.Select(w => w.WarehouseId).ToList());
}

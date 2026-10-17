using System.Security.Claims;
using Ims.Application;
using Ims.Domain;
namespace Ims.Api;

public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public int UserId => int.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    public string Username => Principal?.Identity?.Name ?? "anonymous";
    public string Role => Principal?.FindFirstValue(ClaimTypes.Role) ?? "";
    public IReadOnlyCollection<int> WarehouseIds =>
        Principal?.FindAll(ClaimNames.Warehouse).Select(c => int.TryParse(c.Value, out var i) ? i : 0).Where(i => i > 0).ToList() ?? [];
    public IReadOnlyCollection<string> PermissionCodes => Principal?.FindAll(ClaimNames.Permission).Select(c => c.Value).ToList() ?? [];
    public bool AllWarehouses => bool.TryParse(Principal?.FindFirstValue(ClaimNames.AllWarehouses), out var all) && all;

    public bool CanAccessWarehouse(int warehouseId) => Roles.SeesAllWarehouses(Role) || WarehouseIds.Contains(warehouseId);
}

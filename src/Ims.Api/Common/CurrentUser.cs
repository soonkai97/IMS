using System.Security.Claims;
using Ims.Application;
using Ims.Domain;
namespace Ims.Api;

/// <summary>
/// The caller. Who they are comes from the token; the role and the warehouses come from the access snapshot the bearer event stored for this request, never from the token,
/// so a promotion, a demotion or a changed scope applies without a new sign-in. No snapshot (an anonymous request) means no role and no warehouses.
/// </summary>
public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
    private AccessSnapshot? Access => accessor.HttpContext?.Items[HttpItemKeys.AccessSnapshot] as AccessSnapshot;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public int UserId => int.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    public string Username => Principal?.Identity?.Name ?? "anonymous";
    public string Role => Access?.RoleCode ?? "";
    public IReadOnlyCollection<int> WarehouseIds => Access?.WarehouseIds ?? [];

    public bool CanAccessWarehouse(int warehouseId) => Roles.SeesAllWarehouses(Role) || WarehouseIds.Contains(warehouseId);
}

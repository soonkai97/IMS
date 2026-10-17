using System.Security.Claims;
using Ims.Application;
using Ims.Domain;
namespace Ims.Api;

public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
    private ActorRights? _rights; // one request, one token: the claims are read once

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public int UserId => int.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    public string Username => Principal?.Identity?.Name ?? "anonymous";
    public string Role => Principal?.FindFirstValue(ClaimTypes.Role) ?? "";
    public IReadOnlyCollection<int> WarehouseIds =>
        Principal?.FindAll(ClaimNames.Warehouse).Select(c => int.TryParse(c.Value, out var i) ? i : 0).Where(i => i > 0).ToList() ?? [];
    /// <summary>Every token the API accepts carries the claim (<see cref="ClaimNames.ClaimsVersion"/> is checked at validation); a missing or unreadable one means no.</summary>
    public bool AllWarehouses => bool.TryParse(Principal?.FindFirstValue(ClaimNames.AllWarehouses), out var all) && all;

    public bool CanAccessWarehouse(int warehouseId) => AllWarehouses || WarehouseIds.Contains(warehouseId);

    public ActorRights Rights() => _rights ??= IsAuthenticated
        ? new ActorRights(UserId, EffectivePermissions.Compute(Principal!.FindAll(ClaimNames.Permission).Select(c => c.Value)))
        : new ActorRights(0, EffectivePermissions.None);
}

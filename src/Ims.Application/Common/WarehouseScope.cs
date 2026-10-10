using Ims.Domain;
namespace Ims.Application;

public class WarehouseScope(ICurrentUser current) : IWarehouseScope, IAppService
{
    public IReadOnlyCollection<int>? Ids() => Roles.SeesAllWarehouses(current.Role) ? null : current.WarehouseIds;
}

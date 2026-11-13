namespace Ims.Application;

public sealed class WarehouseScope(ICurrentUser current) : IWarehouseScope, IAppService
{
    public IReadOnlyCollection<int>? Ids() => current.AllWarehouses ? null : current.WarehouseIds;
}

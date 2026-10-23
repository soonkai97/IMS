using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class SalesOrderAccess(ICurrentUser current) : ISalesOrderAccess, IAppService
{
    public void EnsureWarehouse(int warehouseId)
    {
        if (!current.CanAccessWarehouse(warehouseId))
            throw new ValidationException(Messages.WarehouseMissing);
    }

    public void EnsureInScope(int warehouseId)
    {
        if (!current.CanAccessWarehouse(warehouseId))
            throw new NotFoundException(Messages.SalesOrderNotFound);
    }

    public IReadOnlyCollection<int>? WarehouseScope() => current.AllWarehouses ? null : current.WarehouseIds;
}

namespace Ims.Application.Features.Sales;

/// <summary>The warehouse data scope of the current caller for sales orders. What the caller may do with an order beyond reading and drafting arrives with the workflow (PL3.3).</summary>
public interface ISalesOrderAccess
{
    /// <summary>A write names a warehouse the caller may not use: the same 400 as a document.</summary>
    /// <exception cref="ValidationException">The warehouse is outside the caller's scope.</exception>
    void EnsureWarehouse(int warehouseId);

    /// <summary>An order outside the caller's warehouses does not exist for them: 404, like an unknown id.</summary>
    /// <exception cref="NotFoundException">The order's warehouse is outside the caller's scope.</exception>
    void EnsureInScope(int warehouseId);

    /// <summary>The warehouses the caller may read; null means all of them.</summary>
    IReadOnlyCollection<int>? WarehouseScope();
}

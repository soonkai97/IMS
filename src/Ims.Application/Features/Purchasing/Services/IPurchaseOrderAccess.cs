namespace Ims.Application.Features.Purchasing;

/// <summary>The warehouse data scope of the current caller for purchase orders. What a caller may do is decided by the permission on the endpoint (<c>po.read</c>, <c>po.manage</c>).</summary>
public interface IPurchaseOrderAccess
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

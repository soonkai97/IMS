using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>
/// Balances of the given products in one warehouse, always ordered by ProductId. Tracked when they will be changed.
/// Deadlock prevention: every writer acquires and updates balance rows in this same ascending order, so two postings
/// that share products can never wait on each other in a cycle.
/// </summary>
public sealed class BalancesSpec : Specification<StockBalance>
{
    public BalancesSpec(int warehouseId, IReadOnlyCollection<int> productIds, bool tracked)
    {
        Query.Where(b => b.WarehouseId == warehouseId && productIds.Contains(b.ProductId)).OrderBy(b => b.ProductId);
        if (!tracked) Query.AsNoTracking();
    }
}

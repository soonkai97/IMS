using Ardalis.Specification;
using Ims.Domain;
namespace Ims.Application;

/// <summary>True when the product has any stock history. Shared by catalogue and import, which both lock the base unit.</summary>
public sealed class ProductHasMovementsSpec : Specification<StockLedgerEntry>
{
    public ProductHasMovementsSpec(int productId) => Query.Where(l => l.ProductId == productId);
}

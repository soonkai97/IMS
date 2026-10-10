using Ims.Domain;
namespace Ims.Application.Features.Stock;

/// <summary><c>Value</c> orders by quantity times average cost, which would reveal the value to a caller who may not see it: it needs <c>valuation.view</c> (otherwise 400).</summary>
public enum StockSort { Sku = 0, QtyOnHand = 1, [VisibleWith(Permissions.ValuationView)] Value = 2, Warehouse = 3 }

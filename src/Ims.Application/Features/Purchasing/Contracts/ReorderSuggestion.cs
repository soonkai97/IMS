using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>One product to order for one warehouse (see <see cref="ReorderRule"/>). Quantities are in the product's base unit. The product has no preferred supplier in the data model, so the buyer chooses one.</summary>
/// <param name="QtyOnHand">Zero for a product that is stocked in no warehouse the caller can see; negative when stock went below zero.</param>
/// <param name="OnOrderQty">Still to come on the open orders (Draft, Submitted, Approved) of this warehouse: ordered less posted receipts, line by line.</param>
/// <param name="SuggestedQty">Twice the safety level less on hand less on order; always above zero (a row with nothing to order is not listed).</param>
/// <param name="SuggestedValue">The suggested quantity at the warehouse's average cost, in money. Null for a caller without <c>cost.view</c>, and for a product with no average cost yet (never stocked).</param>
public record ReorderSuggestion(Guid ProductId, string Sku, string ProductName, string UomCode, Guid WarehouseId, string WarehouseCode, string WarehouseName,
    decimal QtyOnHand, decimal SafetyStock, decimal OnOrderQty, decimal SuggestedQty, [property: VisibleWith(Permissions.CostView)] decimal? SuggestedValue);

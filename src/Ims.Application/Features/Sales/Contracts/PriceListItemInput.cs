namespace Ims.Application.Features.Sales;

/// <param name="UomId">The unit the price is for: the product's base unit or any unit it converts to; null means the base unit. A price is for exactly this unit and is never converted to another.</param>
/// <param name="Price">The selling price per ONE of the unit, in money: above 0, at most 2 decimal places.</param>
public sealed record PriceListItemInput(Guid ProductId, Guid? UomId, decimal Price);

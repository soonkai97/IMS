namespace Ims.Application.Features.Sales;

/// <param name="CustomerId">The customer of the order. An active or inactive partner that exists; an id that names none is a 400.</param>
/// <param name="ProductId">The product of the line.</param>
/// <param name="UomId">The unit of the line; null means the product's base unit. A price is for exactly this unit: a unit with no item has no price, nothing is converted.</param>
public sealed record PriceLookupFilter(Guid CustomerId, Guid ProductId, Guid? UomId = null);

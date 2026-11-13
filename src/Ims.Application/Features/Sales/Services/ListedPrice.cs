namespace Ims.Application.Features.Sales;

/// <summary>A price a live price list gives, and which list it came from.</summary>
public sealed record ListedPrice(decimal Price, PriceSource Source);

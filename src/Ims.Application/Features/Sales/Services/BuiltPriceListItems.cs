using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>The items of a price list save, with the words (SKU and unit code, by integer key) the audit trail uses to name them.</summary>
public record BuiltPriceListItems(IReadOnlyList<PriceListItem> Items, IReadOnlyDictionary<int, string> Skus, IReadOnlyDictionary<int, string> UomCodes);

namespace Ims.Application.Features.Sales;

public interface IPriceListItemBuilder
{
    /// <summary>Builds the items of a price list. Every item that cannot be built is reported, not only the first.</summary>
    /// <exception cref="ValidationException">One or more items name a product that does not exist or cannot be used, a unit the product does not convert, a price the rules refuse, or a product and unit already named above. Keys are <c>items[i]</c>.</exception>
    Task<BuiltPriceListItems> BuildAsync(IReadOnlyList<PriceListItemInput> inputs, CancellationToken ct);
}

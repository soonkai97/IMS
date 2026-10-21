namespace Ims.Application.Features.Catalog;

/// <summary>Finds which unit conversions of a product are in use, so they can be kept as they are.</summary>
public interface IProductConversionUsage
{
    /// <summary>
    /// Of <paramref name="unitIds"/>, the units whose conversion is in use, as unit id to code: every one when the product has any stock movement (reversed ones count), otherwise those it was ordered in
    /// on a purchase order that is not cancelled. Empty (and no query) when <paramref name="unitIds"/> is empty or the product is not saved yet.
    /// </summary>
    Task<IReadOnlyDictionary<int, string>> UnitsInUseAsync(int productId, IReadOnlyCollection<int> unitIds, CancellationToken ct);
}

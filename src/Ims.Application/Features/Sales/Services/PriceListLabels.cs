namespace Ims.Application.Features.Sales;

/// <summary>The words an audit row uses to name priced items: the SKU of each product and the code of each unit, by integer key. A key that no row has is absent.</summary>
public sealed record PriceListLabels(IReadOnlyDictionary<int, string> Skus, IReadOnlyDictionary<int, string> UomCodes);

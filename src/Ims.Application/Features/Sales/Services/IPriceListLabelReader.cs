namespace Ims.Application.Features.Sales;

public interface IPriceListLabelReader
{
    /// <summary>The SKU of each product and the code of each unit asked for, read in ONE query for the whole save (never one per item).</summary>
    Task<PriceListLabels> ReadAsync(IReadOnlyCollection<int> productIds, IReadOnlyCollection<int> uomIds, CancellationToken ct);
}

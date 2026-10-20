namespace Ims.Application.Features.Catalog;

/// <param name="HsCode">Module accounting. Left out of a save while the module is off (a value is a 400); with the module on, a save replaces both tax-ready fields, so null clears.</param>
public record ProductSaveRequest(string Sku, string Name, Guid? CategoryId, Guid BaseUomId, decimal SafetyStock, bool IsActive,
    IReadOnlyList<string>? Barcodes, IReadOnlyList<UomConversionDto>? Conversions, string? HsCode = null, string? SstCode = null);

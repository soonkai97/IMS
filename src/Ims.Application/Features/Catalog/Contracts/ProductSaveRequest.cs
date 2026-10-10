namespace Ims.Application.Features.Catalog;

public record ProductSaveRequest(string Sku, string Name, int? CategoryId, int BaseUomId, decimal SafetyStock, bool IsActive,
    IReadOnlyList<string>? Barcodes, IReadOnlyList<UomConversionDto>? Conversions);

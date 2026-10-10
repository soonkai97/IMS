namespace Ims.Application.Features.Catalog;

public record ProductDto(int Id, string Sku, string Name, int? CategoryId, string? CategoryName, int BaseUomId, string BaseUomCode,
    decimal SafetyStock, bool IsActive, IReadOnlyList<string> Barcodes, IReadOnlyList<UomConversionDto> Conversions);

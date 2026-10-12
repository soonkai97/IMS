namespace Ims.Application.Features.Catalog;

public record ProductDto(int Id, string Sku, string Name, Guid? CategoryId, string? CategoryName, Guid BaseUomId, string BaseUomCode,
    decimal SafetyStock, bool IsActive, IReadOnlyList<string> Barcodes, IReadOnlyList<UomConversionDto> Conversions);

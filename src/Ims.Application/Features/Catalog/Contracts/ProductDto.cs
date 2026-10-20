using System.Text.Json.Serialization;
namespace Ims.Application.Features.Catalog;

/// <param name="HsCode">Module accounting: left out of the response (not even a null) while the module is off, and when the product has none.</param>
public record ProductDto(Guid Id, string Sku, string Name, Guid? CategoryId, string? CategoryName, Guid BaseUomId, string BaseUomCode,
    decimal SafetyStock, bool IsActive, IReadOnlyList<string> Barcodes, IReadOnlyList<UomConversionDto> Conversions,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? HsCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SstCode = null);

using System.Text.Json.Serialization;
namespace Ims.Application.Features.Catalog;

/// <remarks>
/// The tax-ready fields are not constructor parameters on the wire: a constructor parameter has a default, and the OpenAPI generator cannot describe the default of a struct. They are set from the body only when
/// the key is there, which is how an absent key stays absent. The longer constructor is for code that builds a request in-process (a value converts to a present <see cref="Optional{T}"/>).
/// </remarks>
[method: JsonConstructor]
public record ProductSaveRequest(string Sku, string Name, Guid? CategoryId, Guid BaseUomId, decimal SafetyStock, bool IsActive,
    IReadOnlyList<string>? Barcodes, IReadOnlyList<UomConversionDto>? Conversions)
{
    public ProductSaveRequest(string sku, string name, Guid? categoryId, Guid baseUomId, decimal safetyStock, bool isActive,
        IReadOnlyList<string>? barcodes, IReadOnlyList<UomConversionDto>? conversions, Optional<string> hsCode, Optional<string> sstCode = default)
        : this(sku, name, categoryId, baseUomId, safetyStock, isActive, barcodes, conversions)
    {
        HsCode = hsCode;
        SstCode = sstCode;
    }

    /// <summary>Module accounting. Left out of a save while the module is off (a value is a 400). With the module on: a key that is left out keeps the stored value, an explicit null or blank clears it, a value replaces it. Each of the two fields is decided on its own.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> HsCode { get; init; }

    /// <summary>Module accounting. Same rule as hsCode.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> SstCode { get; init; }
}

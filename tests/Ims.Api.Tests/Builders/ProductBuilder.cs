using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Catalog;

namespace Ims.Api.Tests.Builders;

/// <summary>Builds a valid product request with unique SKU and name; override only what a test cares about.</summary>
public sealed class ProductBuilder
{
    private string _sku = Unique.Sku("P");
    private string _name = Unique.Name("Product");
    private Guid? _categoryId;
    private Guid _baseUomId;
    private decimal _safetyStock;
    private bool _isActive = true;
    private List<string> _barcodes = [];
    private List<UomConversionDto> _conversions = [];
    private string? _hsCode;
    private string? _sstCode;

    public ProductBuilder(ReferenceData reference)
    {
        _categoryId = reference.DefaultCategoryId;
        _baseUomId = reference.PieceUnitId;
    }

    public static ProductBuilder From(ReferenceData reference, ProductDto existing) => new(reference)
    {
        _sku = existing.Sku, _name = existing.Name, _categoryId = existing.CategoryId, _baseUomId = existing.BaseUomId,
        _safetyStock = existing.SafetyStock, _isActive = existing.IsActive, _barcodes = [.. existing.Barcodes],
        _conversions = [.. existing.Conversions], _hsCode = existing.HsCode, _sstCode = existing.SstCode
    };

    public ProductBuilder WithSku(string sku) { _sku = sku; return this; }
    public ProductBuilder WithName(string name) { _name = name; return this; }
    public ProductBuilder WithCategory(Guid? categoryId) { _categoryId = categoryId; return this; }
    public ProductBuilder WithBaseUnit(Guid uomId) { _baseUomId = uomId; return this; }
    public ProductBuilder WithSafetyStock(decimal safetyStock) { _safetyStock = safetyStock; return this; }
    public ProductBuilder WithBarcodes(params string[] barcodes) { _barcodes = [.. barcodes]; return this; }
    /// <summary>The tax-ready fields (what is left of the removed accounting module, part B deletes them); always refused now, there is no module to switch them on.</summary>
    public ProductBuilder WithTaxCodes(string? hsCode, string? sstCode) { _hsCode = hsCode; _sstCode = sstCode; return this; }
    public ProductBuilder Inactive() { _isActive = false; return this; }
    public ProductBuilder Active(bool isActive) { _isActive = isActive; return this; }

    /// <summary>"1 <paramref name="fromUomId"/> = <paramref name="factor"/> base units".</summary>
    public ProductBuilder WithConversion(Guid fromUomId, decimal factor)
    {
        _conversions.Add(new UomConversionDto(fromUomId, _baseUomId, factor));
        return this;
    }

    public ProductSaveRequest Build() => new(_sku, _name, _categoryId, _baseUomId, _safetyStock, _isActive, _barcodes, _conversions, _hsCode, _sstCode);

    public async Task<ProductDto> CreateAsync(ApiClient by) => (await by.Products.CreateAsync(Build())).Data<ProductDto>();
}

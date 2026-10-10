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
    private int? _categoryId;
    private int _baseUomId;
    private decimal _safetyStock;
    private bool _isActive = true;
    private List<string> _barcodes = [];
    private List<UomConversionDto> _conversions = [];

    public ProductBuilder(ReferenceData reference)
    {
        _categoryId = reference.DefaultCategoryId;
        _baseUomId = reference.PieceUnitId;
    }

    public static ProductBuilder From(ReferenceData reference, ProductDto existing) => new(reference)
    {
        _sku = existing.Sku, _name = existing.Name, _categoryId = existing.CategoryId, _baseUomId = existing.BaseUomId,
        _safetyStock = existing.SafetyStock, _isActive = existing.IsActive, _barcodes = [.. existing.Barcodes],
        _conversions = [.. existing.Conversions]
    };

    public ProductBuilder WithSku(string sku) { _sku = sku; return this; }
    public ProductBuilder WithName(string name) { _name = name; return this; }
    public ProductBuilder WithCategory(int? categoryId) { _categoryId = categoryId; return this; }
    public ProductBuilder WithBaseUnit(int uomId) { _baseUomId = uomId; return this; }
    public ProductBuilder WithSafetyStock(decimal safetyStock) { _safetyStock = safetyStock; return this; }
    public ProductBuilder WithBarcodes(params string[] barcodes) { _barcodes = [.. barcodes]; return this; }
    public ProductBuilder Inactive() { _isActive = false; return this; }
    public ProductBuilder Active(bool isActive) { _isActive = isActive; return this; }

    /// <summary>"1 <paramref name="fromUomId"/> = <paramref name="factor"/> base units".</summary>
    public ProductBuilder WithConversion(int fromUomId, decimal factor)
    {
        _conversions.Add(new UomConversionDto(fromUomId, _baseUomId, factor));
        return this;
    }

    public ProductSaveRequest Build() => new(_sku, _name, _categoryId, _baseUomId, _safetyStock, _isActive, _barcodes, _conversions);

    public async Task<ProductDto> CreateAsync(ApiClient by) => (await by.Products.CreateAsync(Build())).Data<ProductDto>();
}

using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Catalog;

public class ProductTests
{
    // ----- Create / Update -----
    [Fact]
    public void Create_TrimsSkuAndNameAndKeepsBaseUnit()
    {
        var category = Category.Create("Drinks").WithId(4);

        var product = Product.Create("  SKU-9  ", "  Cola 330ml ", category, NewUom(), 12m);

        Assert.Equal("SKU-9", product.Sku);
        Assert.Equal("Cola 330ml", product.Name);
        Assert.Equal(4, product.CategoryId);
        Assert.Equal(PcsId, product.BaseUomId);
        Assert.Equal(12m, product.SafetyStock);
        Assert.True(product.IsActive);
    }

    [Theory]
    [InlineData("", "Name")]
    [InlineData("   ", "Name")]
    public void Create_BlankSku_Throws(string sku, string name)
    {
        var ex = Assert.Throws<DomainException>(() => Product.Create(sku, name, null, NewUom(), 0m));

        Assert.Equal(Messages.SkuRequired, ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_BlankName_Throws(string name)
    {
        var ex = Assert.Throws<DomainException>(() => Product.Create("SKU-1", name, null, NewUom(), 0m));

        Assert.Equal(Messages.ProductNameRequired, ex.Message);
    }

    [Fact]
    public void Create_NegativeSafetyStock_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => Product.Create("SKU-1", "N", null, NewUom(), -1m));

        Assert.Equal(Messages.SafetyStockNegative, ex.Message);
    }

    [Fact]
    public void Update_InvalidValues_LeaveTheProductUntouched()
    {
        var product = NewProduct(sku: "SKU-1");

        Assert.Throws<DomainException>(() => product.Update("SKU-2", "New name", null, -5m, true));

        Assert.Equal("SKU-1", product.Sku);
    }

    [Fact]
    public void Update_CanDeactivateAndClearCategory()
    {
        var product = Product.Create("SKU-1", "N", Category.Create("C").WithId(3), NewUom(), 0m);

        product.Update("SKU-1", "N", null, 0m, isActive: false);

        Assert.False(product.IsActive);
        Assert.Null(product.CategoryId);
    }

    // ----- Usable on documents -----
    [Fact]
    public void EnsureUsableOnDocuments_ActiveProduct_DoesNotThrow()
    {
        NewProduct().EnsureUsableOnDocuments();
    }

    [Fact]
    public void EnsureUsableOnDocuments_InactiveProduct_ThrowsNamingTheSku()
    {
        var ex = Assert.Throws<DomainException>(NewProduct(sku: "OLD-7", active: false).EnsureUsableOnDocuments);

        Assert.Equal(Messages.ProductInactive("OLD-7"), ex.Message);
    }

    // ----- Barcodes -----
    [Fact]
    public void SetBarcodes_AddsTrimmedDistinctNonBlankCodes()
    {
        var product = NewProduct();

        product.SetBarcodes([" 111 ", "222", "111", "", "   "]);

        Assert.Equal(["111", "222"], product.Barcodes.Select(b => b.Barcode));
    }

    [Fact]
    public void SetBarcodes_RemovesMissingCodesAndKeepsExistingInstances()
    {
        var product = NewProduct();
        product.SetBarcodes(["111", "222"]);
        var kept = product.Barcodes.Single(b => b.Barcode == "222");

        product.SetBarcodes(["222", "333"]);

        Assert.Equal(["222", "333"], product.Barcodes.Select(b => b.Barcode));
        Assert.Same(kept, product.Barcodes.First());
    }

    [Fact]
    public void SetBarcodes_EmptyList_ClearsAllBarcodes()
    {
        var product = NewProduct();
        product.SetBarcodes(["111"]);

        product.SetBarcodes([]);

        Assert.Empty(product.Barcodes);
    }

    // ----- Conversions -----
    [Fact]
    public void SetConversions_AddsConversionIntoTheBaseUnit()
    {
        var product = NewProduct();

        product.SetConversions([new ConversionDefinition(CartonId, PcsId, 12m)]);

        var conversion = Assert.Single(product.Conversions);
        Assert.Equal(CartonId, conversion.FromUomId);
        Assert.Equal(PcsId, conversion.ToUomId);
        Assert.Equal(12m, conversion.Factor);
    }

    [Fact]
    public void SetConversions_ExistingPair_UpdatesFactorInPlace()
    {
        var product = NewProduct(cartonFactor: 12m);
        var existing = product.Conversions.Single();

        product.SetConversions([new ConversionDefinition(CartonId, PcsId, 24m)]);

        Assert.Same(existing, Assert.Single(product.Conversions));
        Assert.Equal(24m, existing.Factor);
    }

    [Fact]
    public void SetConversions_RemovesConversionsNoLongerListed()
    {
        var product = NewProduct(cartonFactor: 12m);

        product.SetConversions([new ConversionDefinition(3, PcsId, 480m)]);

        var conversion = Assert.Single(product.Conversions);
        Assert.Equal(3, conversion.FromUomId);
    }

    [Fact]
    public void SetConversions_EmptyList_ClearsAll()
    {
        var product = NewProduct(cartonFactor: 12m);

        product.SetConversions([]);

        Assert.Empty(product.Conversions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void SetConversions_NonPositiveFactor_Throws(int factor)
    {
        var product = NewProduct();

        var ex = Assert.Throws<DomainException>(() => product.SetConversions([new ConversionDefinition(CartonId, PcsId, factor)]));

        Assert.Equal(Messages.ConversionInvalid, ex.Message);
    }

    [Fact]
    public void SetConversions_NotLeadingIntoTheBaseUnit_Throws()
    {
        var product = NewProduct();

        var ex = Assert.Throws<DomainException>(() => product.SetConversions([new ConversionDefinition(PcsId, CartonId, 0.1m)]));

        Assert.Equal(Messages.ConversionInvalid, ex.Message);
        Assert.Empty(product.Conversions);
    }

    [Fact]
    public void SetConversions_OneBadEntry_RejectsTheWholeSetAndKeepsTheOldOnes()
    {
        var product = NewProduct(cartonFactor: 12m);

        Assert.Throws<DomainException>(() => product.SetConversions(
            [new ConversionDefinition(CartonId, PcsId, 24m), new ConversionDefinition(3, PcsId, 0m)]));

        Assert.Equal(12m, Assert.Single(product.Conversions).Factor);
    }

    [Fact]
    public void ToBaseQuantity_UsesTheProductsOwnConversions()
    {
        var product = NewProduct(cartonFactor: 12m);

        Assert.Equal(60m, product.ToBaseQuantity(5m, CartonId));
        Assert.Equal(5m, product.ToBaseQuantity(5m, PcsId));
        Assert.Throws<DomainException>(() => product.ToBaseQuantity(1m, 99));
    }

    // ----- Base unit lock (BR-04) -----
    [Fact]
    public void ChangeBaseUnit_WithoutStockMovements_Changes()
    {
        var product = NewProduct();
        var kg = Uom.Create("kg").WithId(7);

        product.ChangeBaseUnit(kg, hasStockMovements: false);

        Assert.Equal(7, product.BaseUomId);
        Assert.Same(kg, product.BaseUom);
    }

    [Fact]
    public void ChangeBaseUnit_WithStockMovements_ThrowsAndKeepsOldUnit()
    {
        var product = NewProduct();

        var ex = Assert.Throws<DomainException>(() => product.ChangeBaseUnit(Uom.Create("kg").WithId(7), hasStockMovements: true));

        Assert.Equal(Messages.BaseUomLocked, ex.Message);
        Assert.Equal(PcsId, product.BaseUomId);
    }

    [Fact]
    public void ChangeBaseUnit_ToTheSameUnitWithMovements_IsANoOp()
    {
        var product = NewProduct();

        product.ChangeBaseUnit(NewUom(), hasStockMovements: true);

        Assert.Equal(PcsId, product.BaseUomId);
    }
}

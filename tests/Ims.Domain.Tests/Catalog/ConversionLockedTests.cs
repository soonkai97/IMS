using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Catalog;

/// <summary>A conversion that exists cannot get another factor or be removed while it is in use; a new one, an unchanged one and an unused one are free.</summary>
public class ConversionLockedTests
{
    private static readonly IReadOnlyDictionary<int, string> CartonInUse = new Dictionary<int, string> { [CartonId] = "CARTON" };

    private static ConversionDefinition Carton(decimal factor) => new(CartonId, PcsId, factor);

    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    public void ChangedFactor_OfAUnitInUse_IsRefused(double factor)
    {
        var product = NewProduct(cartonFactor: 12m);

        var ex = Assert.Throws<DomainException>(() => product.SetConversions([Carton((decimal)factor)], CartonInUse));

        Assert.Equal(Messages.ConversionLocked("CARTON"), ex.Message);
        Assert.Equal(12m, product.Conversions.Single().Factor);
    }

    [Fact]
    public void FactorThatRoundsToTheStoredOne_IsNotAChange()
    {
        var product = NewProduct(cartonFactor: 12m);

        product.SetConversions([Carton(12.0000004m)], CartonInUse); // beyond the stored 6 decimals

        Assert.Equal(12m, product.Conversions.Single().Factor);
    }

    [Fact]
    public void Removal_OfAUnitInUse_IsRefused()
    {
        var product = NewProduct(cartonFactor: 12m);

        var ex = Assert.Throws<DomainException>(() => product.SetConversions([], CartonInUse));

        Assert.Equal(Messages.ConversionLocked("CARTON"), ex.Message);
        Assert.Single(product.Conversions);
    }

    [Fact]
    public void UnchangedList_OfAUnitInUse_IsAccepted()
    {
        var product = NewProduct(cartonFactor: 12m);

        product.SetConversions([Carton(12m)], CartonInUse);

        Assert.Equal(12m, product.Conversions.Single().Factor);
    }

    [Fact]
    public void AddingAConversion_NextToOneInUse_IsAccepted()
    {
        var product = NewProduct(cartonFactor: 12m);

        product.SetConversions([Carton(12m), new ConversionDefinition(3, PcsId, 480m)], CartonInUse);

        Assert.Equal(2, product.Conversions.Count);
    }

    [Fact]
    public void ChangeOrRemoval_OfAUnitNotInUse_IsAccepted()
    {
        var product = NewProduct(cartonFactor: 12m);
        var otherUnitInUse = new Dictionary<int, string> { [3] = "PALLET" };

        product.SetConversions([Carton(24m)], otherUnitInUse);
        product.SetConversions([], otherUnitInUse);

        Assert.Empty(product.Conversions);
    }

    [Fact]
    public void ChangedConversionUnits_ListsChangedAndRemovedButNotNewOrUnchanged()
    {
        var product = NewProduct(cartonFactor: 12m);
        product.SetConversions([Carton(12m), new ConversionDefinition(3, PcsId, 480m)]);

        Assert.Empty(product.ChangedConversionUnits([Carton(12m), new ConversionDefinition(3, PcsId, 480m), new ConversionDefinition(4, PcsId, 5m)]));
        Assert.Equal([CartonId], product.ChangedConversionUnits([Carton(10m), new ConversionDefinition(3, PcsId, 480m)]));
        Assert.Equal([CartonId, 3], product.ChangedConversionUnits([]));
    }

    [Fact]
    public void ChangeBaseUnit_DroppingAConversionInUse_IsRefused()
    {
        var product = NewProduct(cartonFactor: 12m);

        var ex = Assert.Throws<DomainException>(() => product.ChangeBaseUnit(Uom.Create("kg").WithId(7), hasStockMovements: false, CartonInUse));

        Assert.Equal(Messages.ConversionLocked("CARTON"), ex.Message);
        Assert.Equal(PcsId, product.BaseUomId);
        Assert.Single(product.Conversions);
    }

    [Fact]
    public void ChangeBaseUnit_WithConversionsNotInUse_DropsThem()
    {
        var product = NewProduct(cartonFactor: 12m);

        product.ChangeBaseUnit(Uom.Create("kg").WithId(7), hasStockMovements: false, new Dictionary<int, string>());

        Assert.Empty(product.Conversions);
    }
}

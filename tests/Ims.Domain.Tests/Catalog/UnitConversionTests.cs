namespace Ims.Domain.Tests.Catalog;

public class UnitConversionTests
{
    private const int Piece = 1;
    private const int Carton = 2;
    private const int Pallet = 3;

    private static readonly UomConversion[] Conversions =
    [
        UomConversion.Create(Carton, Piece, 12m),
        UomConversion.Create(Pallet, Piece, 480m),
        UomConversion.Create(Piece, Carton, 99m), // wrong direction: must never be used for a base-unit lookup
    ];

    [Fact]
    public void ToBase_SameUnit_ReturnsQuantityUnchanged()
    {
        Assert.Equal(7.5m, UnitConversion.ToBase(7.5m, Piece, Piece, []));
    }

    [Fact]
    public void ToBase_CartonToPiece_MultipliesByFactor()
    {
        Assert.Equal(36m, UnitConversion.ToBase(3m, Carton, Piece, Conversions));
    }

    [Fact]
    public void ToBase_PicksTheConversionForTheEnteredUnit()
    {
        Assert.Equal(960m, UnitConversion.ToBase(2m, Pallet, Piece, Conversions));
    }

    [Fact]
    public void ToBase_FractionalQuantity_ConvertsExactly()
    {
        Assert.Equal(6m, UnitConversion.ToBase(0.5m, Carton, Piece, Conversions));
    }

    [Fact]
    public void ToBase_MissingConversion_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => UnitConversion.ToBase(1m, uomId: 9, baseUomId: Piece, Conversions));

        Assert.Equal(Messages.ConversionMissing, ex.Message);
    }

    [Fact]
    public void ToBase_OnlyConversionGoesTheWrongWay_Throws()
    {
        // Only Piece -> Carton exists; converting Carton into base Piece has no definition.
        UomConversion[] wrongWay = [UomConversion.Create(Piece, Carton, 0.1m)];

        Assert.Throws<DomainException>(() => UnitConversion.ToBase(1m, Carton, Piece, wrongWay));
    }

    [Fact]
    public void UomConversion_NonPositiveFactor_IsRejected()
    {
        var ex = Assert.Throws<DomainException>(() => UomConversion.Create(Carton, Piece, 0m));

        Assert.Equal(Messages.ConversionFactorInvalid, ex.Message);
    }

    [Fact]
    public void UomConversion_ChangeFactor_RejectsNegativeAndKeepsOldValue()
    {
        var conversion = UomConversion.Create(Carton, Piece, 12m);

        Assert.Throws<DomainException>(() => conversion.ChangeFactor(-1m));

        Assert.Equal(12m, conversion.Factor);
    }
}

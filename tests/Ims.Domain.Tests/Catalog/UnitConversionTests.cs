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

    // ---- Parity: the conversion is the Quantity rule (DDD-03). The reference below is the formula as it stood before ToBase delegated to Quantity. ----

    private static decimal Reference(decimal qty, decimal factor) => Math.Round(qty * factor, Limits.QuantityScale, MidpointRounding.AwayFromZero);

    private static readonly decimal[] Pow10 = [1m, 10m, 100m, 1_000m, 10_000m, 100_000m, 1_000_000m];

    private static bool SameBits(decimal a, decimal b) => decimal.GetBits(a).SequenceEqual(decimal.GetBits(b));

    /// <summary>A few thousand (quantity, factor) pairs: random quantities of 0 to 4 decimals (some negative), random factors of 0 to 6 decimals, then the pairs whose product ends on exactly half a step.</summary>
    private static IEnumerable<(decimal Qty, decimal Factor)> Grid()
    {
        var random = new Random(20261114);
        for (var i = 0; i < 4000; i++)
        {
            var qtyScale = random.Next(0, 5);
            var factorScale = random.Next(0, 7);
            var qty = random.NextInt64(0, 1_000_000) / Pow10[qtyScale] * (random.Next(8) == 0 ? -1m : 1m);
            var factor = random.NextInt64(1, 10_000_000) / Pow10[factorScale];
            yield return (qty, factor);
        }

        // Products that end on exactly half of the smallest step: a to-even rounding would differ from half away from zero here.
        for (var steps = 1; steps <= 400; steps++)
        {
            yield return (steps * 0.0001m, 0.5m);
            yield return (-steps * 0.0001m, 0.5m);
            yield return (steps * 0.0001m, 1.5m);
            yield return (steps * 0.00005m, 1m);
        }
    }

    [Fact]
    public void Quantity_TimesAFactor_EqualsToBase_ToTheLastPlace_OverAGridOfPairs()
    {
        var pairs = Grid().ToList();
        Assert.True(pairs.Count >= 5000);

        var mismatches = new List<string>();
        foreach (var (qty, factor) in pairs)
        {
            var expected = Reference(qty, factor);
            var viaToBase = UnitConversion.ToBase(qty, Carton, Piece, [UomConversion.Create(Carton, Piece, factor)]);
            var viaConverted = Quantity.Converted(qty, factor).Value;

            if (!SameBits(expected, viaToBase) || !SameBits(expected, viaConverted)) mismatches.Add($"{qty} x {factor}: reference {expected}, ToBase {viaToBase}, Converted {viaConverted}");
            if (qty == decimal.Round(qty, Limits.QuantityScale) && !SameBits(expected, Quantity.Signed(qty).Times(factor).Value)) mismatches.Add($"{qty} x {factor}: Times differs");
        }

        Assert.True(mismatches.Count == 0, $"{mismatches.Count} pairs differ, first: {string.Join(" | ", mismatches.Take(5))}");
    }

    [Fact]
    public void ToBase_SameUnit_StaysUnroundedAndUnchecked_SoTheCallersKeepTheirOwnLimitChecks()
    {
        Assert.Equal(1.234567m, UnitConversion.ToBase(1.234567m, Piece, Piece, []));
        Assert.Equal(1_000_000_000_000_000m, UnitConversion.ToBase(1_000_000_000_000_000m, Piece, Piece, []));
    }

    [Fact]
    public void ToBase_AResultBeyondTheLimit_IsRefusedWithTheQuantitySentence()
    {
        var ex = Assert.Throws<DomainException>(() => UnitConversion.ToBase(1_000_000_000m, Carton, Piece, [UomConversion.Create(Carton, Piece, 1_000_000m)]));

        Assert.Equal(Messages.QtyTooLarge, ex.Message);
    }
}

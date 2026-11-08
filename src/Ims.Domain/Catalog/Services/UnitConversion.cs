namespace Ims.Domain;

public static class UnitConversion
{
    /// <summary>
    /// Converts a quantity entered in <paramref name="uomId"/> to the base unit, rounded to the four decimals the stored quantity has (half away from zero, as SQL Server converts into the column), so that the value
    /// in memory is the value that is stored and read back: the sum of stored base quantities then equals the sum of the quantities the code added up.
    /// </summary>
    public static decimal ToBase(decimal qty, int uomId, int baseUomId, IEnumerable<UomConversion> conversions)
    {
        if (uomId == baseUomId) return qty;
        var c = conversions.FirstOrDefault(x => x.FromUomId == uomId && x.ToUomId == baseUomId)
                ?? throw new DomainException(Messages.ConversionMissing);
        if (c.Factor <= 0) throw new DomainException(Messages.ConversionFactorInvalid);
        return Math.Round(qty * c.Factor, Limits.QuantityScale, MidpointRounding.AwayFromZero);
    }
}

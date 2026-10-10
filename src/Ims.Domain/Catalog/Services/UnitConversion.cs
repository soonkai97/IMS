namespace Ims.Domain;

public static class UnitConversion
{
    /// <summary>Converts a quantity entered in <paramref name="uomId"/> to the base unit.</summary>
    public static decimal ToBase(decimal qty, int uomId, int baseUomId, IEnumerable<UomConversion> conversions)
    {
        if (uomId == baseUomId) return qty;
        var c = conversions.FirstOrDefault(x => x.FromUomId == uomId && x.ToUomId == baseUomId)
                ?? throw new DomainException(Messages.ConversionMissing);
        if (c.Factor <= 0) throw new DomainException(Messages.ConversionFactorInvalid);
        return qty * c.Factor;
    }
}

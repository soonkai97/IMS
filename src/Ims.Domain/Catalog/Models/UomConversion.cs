namespace Ims.Domain;

/// <summary>Quantity in FromUom x Factor = quantity in ToUom (BR-04: stock itself is always held in the base unit).</summary>
public class UomConversion : Entity
{
    private UomConversion() { }

    public int ProductId { get; private set; }
    public int FromUomId { get; private set; }
    public int ToUomId { get; private set; }

    // Navigation properties used by read-side projections.
    public Uom FromUom { get; private set; } = null!;
    public Uom ToUom { get; private set; } = null!;
    public decimal Factor { get; private set; }

    public static UomConversion Create(int fromUomId, int toUomId, decimal factor)
    {
        var c = new UomConversion { FromUomId = fromUomId, ToUomId = toUomId };
        c.ChangeFactor(factor);
        return c;
    }

    public void ChangeFactor(decimal factor)
    {
        if (factor <= 0) throw new DomainException(Messages.ConversionFactorInvalid);
        Factor = factor;
    }
}

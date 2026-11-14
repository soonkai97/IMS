using System.Globalization;

namespace Ims.Domain;

/// <summary>
/// An amount of stock in one unit: at most <see cref="Limits.QuantityScale"/> (4) decimals, never beyond <see cref="Limits.MaxBaseQuantity"/> either way (the column is <c>decimal(18,4)</c>), compared by value
/// (12.5 and 12.50 are the same quantity). A conversion that makes a fifth decimal rounds half away from zero, as SQL Server converts into the column, so the value in memory is the value that is stored and read back.
/// <para>
/// The sign depends on the use, so there is one factory for each: <see cref="Positive"/> for a line, <see cref="NonNegative"/> for a threshold or a safety level, <see cref="Signed"/> for a balance or a delta (negative
/// stock is allowed, BR-03). A factory refuses with a sentence that already exists in <see cref="Messages"/>; the caller may pass its own where it has one.
/// </para>
/// </summary>
public readonly record struct Quantity : IValueObject, IComparable<Quantity>
{
    private Quantity(decimal value) => Value = value;

    /// <summary>The amount, at most four decimals. A default quantity is zero.</summary>
    public decimal Value { get; }

    public static Quantity Zero => default;

    public bool IsZero => Value == 0m;

    public bool IsPositive => Value > 0m;

    public bool IsNegative => Value < 0m;

    /// <summary>A balance or a delta: any sign, zero included.</summary>
    public static Quantity Signed(decimal value) => new(Checked(value));

    /// <summary>A quantity on a line: above zero. <paramref name="refusal"/> is the sentence for zero or a negative (the line sentence by default).</summary>
    public static Quantity Positive(decimal value, string refusal = Messages.QtyPositive)
    {
        if (value <= 0m) throw new DomainException(refusal);
        return new(Checked(value));
    }

    /// <summary>A threshold or a safety level: zero or more. <paramref name="refusal"/> is the sentence for a negative, which differs by use.</summary>
    public static Quantity NonNegative(decimal value, string refusal)
    {
        if (value < 0m) throw new DomainException(refusal);
        return new(Checked(value));
    }

    /// <summary>A computed value kept at four decimals, half away from zero (0.00005 becomes 0.0001, -0.00005 becomes -0.0001).</summary>
    public static Quantity Rounded(decimal value) => new(Limited(Round(value)));

    /// <summary>
    /// An entered quantity in some unit times that unit's factor, as a quantity in the base unit, rounded once. The entered value is a plain number (any scale, any sign): rounding it first and the product again
    /// would give another result, which is what <c>UnitConversion.ToBase</c> never did.
    /// </summary>
    public static Quantity Converted(decimal entered, decimal factor)
    {
        if (factor <= 0m) throw new DomainException(Messages.ConversionFactorInvalid);
        return Rounded(entered * factor);
    }

    /// <summary>This quantity in a base unit that holds <paramref name="factor"/> of it (above zero), at four decimals.</summary>
    public Quantity Times(decimal factor) => Converted(Value, factor);

    public Quantity Plus(Quantity other) => new(Limited(Value + other.Value));

    public Quantity Minus(Quantity other) => new(Limited(Value - other.Value));

    public int CompareTo(Quantity other) => Value.CompareTo(other.Value);

    public static bool operator <(Quantity left, Quantity right) => left.CompareTo(right) < 0;

    public static bool operator <=(Quantity left, Quantity right) => left.CompareTo(right) <= 0;

    public static bool operator >(Quantity left, Quantity right) => left.CompareTo(right) > 0;

    public static bool operator >=(Quantity left, Quantity right) => left.CompareTo(right) >= 0;

    /// <summary>The value with up to four decimals and no culture, as the messages show a quantity (<c>12.5</c>, <c>-0.0001</c>).</summary>
    public override string ToString() => Value.ToString("0.####", CultureInfo.InvariantCulture);

    private static decimal Round(decimal value) => Math.Round(value, Limits.QuantityScale, MidpointRounding.AwayFromZero);

    private static decimal Limited(decimal value) =>
        Math.Abs(value) > Limits.MaxBaseQuantity ? throw new DomainException(Messages.QtyTooLarge) : value;

    /// <summary>An entered value passes as it is when it has no more than four decimals (trailing zeros are not decimals) and is within the limit: the same rule as the request validators' precision check.</summary>
    private static decimal Checked(decimal value) => Round(value) == value ? Limited(value) : throw new DomainException(Messages.QtyTooLarge);
}

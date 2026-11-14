using System.Globalization;

namespace Ims.Domain;

/// <summary>
/// How many base units one unit holds (BR-04): above zero, and compared at <see cref="Limits.FactorScale"/> (6) places, the scale of its column <c>decimal(18,6)</c>, so that two factors that round to the same value
/// are the same factor (<see cref="SameAs"/>, <see cref="Scaled(decimal)"/>). The factor itself is kept as it was given: a conversion multiplies by it and rounds once (<see cref="Quantity.Times(ConversionFactor)"/>), which
/// is what it always did; <see cref="Stored"/> is what the column will hold. A default factor is zero and not a factor: only <see cref="Of"/> makes one, and a conversion refuses the default.
/// </summary>
public readonly record struct ConversionFactor : IValueObject
{
    private ConversionFactor(decimal value) => Value = value;

    /// <summary>The factor as given, above zero.</summary>
    public decimal Value { get; }

    /// <summary>The factor at the stored scale: <see cref="Limits.FactorScale"/> places, half away from zero.</summary>
    public decimal Stored => Scaled(Value);

    /// <summary>A factor above zero, or a <see cref="DomainException"/> (<see cref="Messages.ConversionFactorInvalid"/>).</summary>
    public static ConversionFactor Of(decimal value) => value > 0m ? new(value) : throw new DomainException(Messages.ConversionFactorInvalid);

    /// <summary>
    /// A factor at the stored scale, half away from zero (0.0000005 becomes 0.000001, as SQL Server converts into the column). A plain rounding that never refuses: a save looks at definitions that
    /// nobody has validated yet (<see cref="Product.ChangedConversionUnits"/>).
    /// </summary>
    public static decimal Scaled(decimal factor) => Math.Round(factor, Limits.FactorScale, MidpointRounding.AwayFromZero);

    /// <summary>True when the two factors are the same at the stored scale: 12 and 12.0000004 are, 12 and 12.0000005 are not.</summary>
    public bool SameAs(ConversionFactor other) => Stored == other.Stored;

    public override string ToString() => Value.ToString("0.######", CultureInfo.InvariantCulture);
}

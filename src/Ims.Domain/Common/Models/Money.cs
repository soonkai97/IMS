namespace Ims.Domain;

/// <summary>
/// The one rule for money: exactly <see cref="Decimals"/> decimal places, never a fraction of a cent, and one rounding mode for everything a person sees.
/// <list type="bullet">
/// <item><b>Money (2 decimals):</b> a line amount, a document total, an estimated amount, a stock value, an approval threshold, and the cost per ENTERED unit that a person types.</item>
/// <item><b>Unit prices (<see cref="UnitPriceDecimals"/> = 4 decimals):</b> a DERIVED cost per base unit, shown as <c>baseUnitCost</c>, a stock row's <c>avgCost</c>, a ledger or movement <c>unitCost</c>
/// and the Excel columns "Avg cost" and "Unit cost". A carton priced 3.00 that holds 1,000 pieces costs 0.0030 a piece; at 2 decimals it would show 0.00.
/// Amounts computed from a unit price are still money: 1,000 x 0.0030 is 3.00.</item>
/// <item><b>Quantities</b> are neither and keep the precision of their unit.</item>
/// </list>
/// <para>
/// A cost per base unit and the moving average keep more places inside the system so that repeated receipts and unit conversions do not drift; what leaves the
/// system is rounded to the places above, half away from zero.
/// </para>
/// <para>
/// Money is also a value object (<see cref="IValueObject"/>): an amount with exactly two decimals, compared by value (12.5 equals 12.50), created only through <see cref="Exact"/> (refuses a fraction of a cent),
/// <see cref="Rounded"/> or <see cref="Of"/>, and never changed in place. It may be negative (the value of negative stock, BR-03). The static members and constants below are the same rules over a bare <c>decimal</c>
/// and keep their signatures; the value object stays inside the Domain and Application, and the wire and the columns keep a <c>decimal</c> (<see cref="Value"/>).
/// </para>
/// </summary>
public readonly record struct Money : IValueObject, IComparable<Money>
{
    public const int Decimals = 2;

    /// <summary>Places of a derived cost per base unit on the API and in Excel.</summary>
    public const int UnitPriceDecimals = 4;

    /// <summary>
    /// Half a cent and more rounds away from zero (0.005 becomes 0.01, -0.005 becomes -0.01). SQL Server's ROUND on a decimal does the same, so a value rounded in a
    /// query (<c>Math.Round(x, Money.Decimals)</c> is translated to ROUND) equals the value rounded here; <c>MoneyPrecisionTests</c> pins the two together.
    /// </summary>
    public const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    // The only constructor: it rounds, so a Money always holds exactly two decimal places. Exact refuses a fraction of a cent before it gets here.
    private Money(decimal value) => Value = Round(value) + 0.00m;

    /// <summary>The amount as a <c>decimal</c> with exactly two decimal places (the form of the column, the wire and the Excel cell).</summary>
    public decimal Value { get; }

    /// <summary>No money.</summary>
    public static Money Zero => new(0m);

    /// <summary>The amount, or a <see cref="DomainException"/> with <paramref name="refusal"/> when it has a fraction of a cent (12.5, 12.50 and 12.500 are fine, 12.505 is not). The caller names the sentence, so each rule keeps its own message.</summary>
    public static Money Exact(decimal value, string refusal) => IsExact(value) ? new Money(value) : throw new DomainException(refusal);

    /// <summary>The amount rounded to a cent, half away from zero.</summary>
    public static Money Rounded(decimal value) => new(value);

    /// <summary>The value of <paramref name="baseQty"/> units at <paramref name="unitCost"/>, rounded to a cent once (<see cref="Amount"/> as a value object).</summary>
    public static Money Of(decimal baseQty, decimal unitCost) => new(baseQty * unitCost);

    public Money Plus(Money other) => new(Value + other.Value);

    public Money Minus(Money other) => new(Value - other.Value);

    public static Money operator +(Money left, Money right) => left.Plus(right);

    public static Money operator -(Money left, Money right) => left.Minus(right);

    public static bool operator <(Money left, Money right) => left.Value < right.Value;

    public static bool operator >(Money left, Money right) => left.Value > right.Value;

    public static bool operator <=(Money left, Money right) => left.Value <= right.Value;

    public static bool operator >=(Money left, Money right) => left.Value >= right.Value;

    public int CompareTo(Money other) => Value.CompareTo(other.Value);

    /// <summary>True when the amount fits a line: not above <see cref="Limits.MaxLineAmount"/>, the largest amount of the <c>decimal(18,2)</c> columns that the code keeps clear of.</summary>
    public bool IsWithinLineLimit => Value <= Limits.MaxLineAmount;

    /// <summary>The same amount, or a <see cref="DomainException"/> (<see cref="Messages.LineAmountTooLarge"/>) when it is above <see cref="Limits.MaxLineAmount"/>: a 400 with a sentence, never a database overflow.</summary>
    public Money EnsureWithinLineLimit() => IsWithinLineLimit ? this : throw new DomainException(Messages.LineAmountTooLarge);

    // ----- The same rules over a bare decimal. Existing call sites use these; the signatures do not change. -----

    public static decimal Round(decimal value) => Math.Round(value, Decimals, Rounding);

    /// <summary>A derived cost per base unit as it is shown: 4 decimals, half away from zero.</summary>
    public static decimal RoundUnitPrice(decimal value) => Math.Round(value, UnitPriceDecimals, Rounding);

    /// <summary>Rounds to <paramref name="places"/> (2 for money, 4 for a unit price).</summary>
    public static decimal Round(decimal value, int places) => Math.Round(value, places, Rounding);

    /// <summary>True when the value has no fraction of a cent: 12.5 and 12.50 and 12.500 are fine, 12.505 is not.</summary>
    public static bool IsExact(decimal value) => Round(value) == value;

    /// <summary>The value of <paramref name="baseQty"/> units at <paramref name="unitCost"/>, rounded to a cent. A document total is the sum of these.</summary>
    public static decimal Amount(decimal baseQty, decimal unitCost) => Round(baseQty * unitCost);
}

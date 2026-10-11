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
/// </summary>
public static class Money
{
    public const int Decimals = 2;

    /// <summary>Places of a derived cost per base unit on the API and in Excel.</summary>
    public const int UnitPriceDecimals = 4;

    /// <summary>
    /// Half a cent and more rounds away from zero (0.005 becomes 0.01, -0.005 becomes -0.01). SQL Server's ROUND on a decimal does the same, so a value rounded in a
    /// query (<c>Math.Round(x, Money.Decimals)</c> is translated to ROUND) equals the value rounded here; <c>MoneyPrecisionTests</c> pins the two together.
    /// </summary>
    public const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

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

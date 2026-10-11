namespace Ims.Domain;

/// <summary>
/// The one rule for money: exactly <see cref="Decimals"/> decimal places, never a fraction of a cent, and one rounding mode everywhere.
/// Money is a unit cost, an average cost, a line amount, a document total, an estimated amount, a stock value, an approval threshold.
/// Quantities are not money and keep the precision of their unit.
/// <para>
/// A cost per base unit and the moving average may keep more places inside the system so that repeated
/// receipts and unit conversions do not drift; every amount derived from them is rounded here, and every value a person sees is rounded here.
/// </para>
/// </summary>
public static class Money
{
    public const int Decimals = 2;

    /// <summary>
    /// Half a cent and more rounds away from zero (0.005 becomes 0.01, -0.005 becomes -0.01). SQL Server's ROUND on a decimal does the same, so a value rounded in a
    /// query (<c>Math.Round(x, Money.Decimals)</c> is translated to ROUND) equals the value rounded here; <c>MoneyPrecisionTests</c> pins the two together.
    /// </summary>
    public const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    public static decimal Round(decimal value) => Math.Round(value, Decimals, Rounding);

    /// <summary>True when the value has no fraction of a cent: 12.5 and 12.50 and 12.500 are fine, 12.505 is not.</summary>
    public static bool IsExact(decimal value) => Round(value) == value;

    /// <summary>The value of <paramref name="baseQty"/> units at <paramref name="unitCost"/>, rounded to a cent. A document total is the sum of these.</summary>
    public static decimal Amount(decimal baseQty, decimal unitCost) => Round(baseQty * unitCost);
}

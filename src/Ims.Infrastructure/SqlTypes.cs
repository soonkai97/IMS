namespace Ims.Infrastructure;

public static class SqlTypes
{
    public const string Quantity = "decimal(18,4)";
    /// <summary>A cost per base unit and the moving average, kept with <c>Limits.CostDecimals</c> places inside the system; everything shown to a person is money, rounded to 2 places.</summary>
    public const string Cost = "decimal(18,10)";
    /// <summary>The multiplier of a unit conversion (1 carton = 12 pieces).</summary>
    public const string Factor = "decimal(18,6)";
    /// <summary>An amount of money: exactly 2 decimal places (approval thresholds).</summary>
    public const string Money = "decimal(18,2)";
}

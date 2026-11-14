using System.Globalization;

namespace Ims.Domain;

/// <summary>
/// A cost per BASE unit (BR-05): never negative, never above <see cref="Limits.MaxCostPerBaseUnit"/> (the column is <c>decimal(22,10)</c>), and kept at <see cref="Limits.CostDecimals"/> (10) places inside the system,
/// half away from zero, so that the value in memory is the value stored and read back. It is an INTERNAL number: what a person sees is a unit price at four places (<see cref="Shown"/>) and an amount computed from it is
/// money (<see cref="Money.Of(decimal, decimal)"/>). Compared by value (2.5 and 2.50 are the same cost); a default cost is zero.
/// <para>
/// <see cref="PerBaseUnit"/> and <see cref="Blend"/> are the two computations of the moving average cost: the cost of an entered unit spread over the base units it holds, and the weighted average that
/// <see cref="StockBalance.Receive"/> keeps. Both do exactly what the decimal code did before this type existed; the stock oracle and <c>UnitCostBlendOracleTests</c> pin that. Refusals reuse the sentences of
/// <see cref="Messages"/>.
/// </para>
/// </summary>
public readonly record struct UnitCost : IValueObject, IComparable<UnitCost>
{
    private UnitCost(decimal value) => Value = value;

    /// <summary>The cost per base unit, at most ten decimals. A default cost is zero.</summary>
    public decimal Value { get; }

    public static UnitCost Zero => default;

    public bool IsZero => Value == 0m;

    /// <summary>The cost as it is shown: a unit price with <see cref="Money.UnitPriceDecimals"/> (4) places, half away from zero.</summary>
    public decimal Shown => Money.RoundUnitPrice(Value);

    /// <summary>
    /// A cost from a decimal: kept at ten places half away from zero (as SQL Server converts into the column), then refused when negative (<see cref="Messages.CostNegative"/>) or above
    /// <see cref="Limits.MaxCostPerBaseUnit"/> (<see cref="Messages.CostTooLargeForUnit"/>, a domain error, never a database overflow).
    /// </summary>
    public static UnitCost Of(decimal value)
    {
        var kept = Math.Round(value, Limits.CostDecimals, Limits.CostRounding);
        if (kept < 0m) throw new DomainException(Messages.CostNegative);
        return kept > Limits.MaxCostPerBaseUnit ? throw new DomainException(Messages.CostTooLargeForUnit) : new(kept);
    }

    /// <summary>
    /// The cost per base unit of a cost entered per unit: <c>enteredCostPerUnit x enteredQty / baseQty</c>, so the value of the line is the same in either unit (3 cartons at 120.00 are 36 pieces at 10.00). The quantities
    /// are plain numbers because an entered quantity has any scale; the caller has seen that the base quantity is above zero.
    /// </summary>
    public static UnitCost PerBaseUnit(decimal enteredCostPerUnit, decimal enteredQty, decimal baseQty) => Of(enteredCostPerUnit * enteredQty / baseQty);

    /// <summary>
    /// The average cost after <paramref name="incoming"/> units come in at <paramref name="incomingCost"/> on top of <paramref name="onHand"/> units valued at <paramref name="average"/> (BR-05).
    /// While the stock is zero or negative, or the receipt leaves it so, the old average means nothing and the new cost wins. Stock that comes in at the average itself (the return of an issue) leaves it exactly as it is,
    /// instead of deriving it again and drifting in the last place. Otherwise the weighted average, rounded to ten places half away from zero; it is between the two costs, so it is never above the column.
    /// </summary>
    public static UnitCost Blend(Quantity onHand, UnitCost average, Quantity incoming, UnitCost incomingCost)
    {
        var total = onHand.Value + incoming.Value;
        if (onHand.Value <= 0m || total <= 0m) return incomingCost;
        if (incomingCost == average) return average;
        return new(Math.Round((onHand.Value * average.Value + incoming.Value * incomingCost.Value) / total, Limits.CostDecimals, Limits.CostRounding));
    }

    public int CompareTo(UnitCost other) => Value.CompareTo(other.Value);

    public static bool operator <(UnitCost left, UnitCost right) => left.CompareTo(right) < 0;

    public static bool operator <=(UnitCost left, UnitCost right) => left.CompareTo(right) <= 0;

    public static bool operator >(UnitCost left, UnitCost right) => left.CompareTo(right) > 0;

    public static bool operator >=(UnitCost left, UnitCost right) => left.CompareTo(right) >= 0;

    /// <summary>The value with up to ten decimals and no culture.</summary>
    public override string ToString() => Value.ToString("0.##########", CultureInfo.InvariantCulture);
}

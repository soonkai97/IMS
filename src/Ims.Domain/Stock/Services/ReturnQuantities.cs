namespace Ims.Domain;

/// <summary>
/// The arithmetic of returning goods of a Posted document, in one place (plan D13), the twin of <see cref="SalesOrderQuantities"/>. The returned quantity is COMPUTED, never stored: the sum over the Posted
/// returns of the document, per original line position. A Draft, Submitted, Approved, Cancelled or Reversed return counts as nothing, so a reversal gives its quantity back without any code. A return line uses
/// the original line's own unit, so every figure is exact (four decimals, added up without rounding) and one measure decides what remains and what is over.
/// </summary>
public static class ReturnQuantities
{
    /// <summary>What is left of an original quantity: never below zero.</summary>
    public static decimal Remaining(decimal original, decimal returned) => Math.Max(original - returned, 0m);

    /// <summary>The original lines a return asks too much of. Lines of the return that name the same original line are added up first, so splitting a quantity over two lines does not get past the check. A position the original does not have is not judged here (the caller refuses it with its own sentence).</summary>
    public static IReadOnlyList<OverReturn> OverReturns(IReadOnlyList<(int LineNo, StockDocumentLine Line)> originalLines, IReadOnlyDictionary<int, ReturnedQuantity> returned, IEnumerable<ReturnQuantity> returnLines)
    {
        var over = new List<OverReturn>();
        foreach (var group in returnLines.GroupBy(l => l.LineNo).OrderBy(g => g.Key))
        {
            var original = originalLines.FirstOrDefault(l => l.LineNo == group.Key);
            if (original.Line is null) continue;
            var already = returned.GetValueOrDefault(group.Key);
            var remaining = Remaining(original.Line.Qty, already.Qty);
            if (group.Sum(l => l.Qty) <= remaining) continue;
            over.Add(new OverReturn(group.Key, original.Line.Qty, already.Qty, remaining, group.Sum(l => l.Qty)));
        }
        return over;
    }
}

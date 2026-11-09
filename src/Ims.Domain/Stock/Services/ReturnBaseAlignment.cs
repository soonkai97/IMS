namespace Ims.Domain;

/// <summary>
/// Keeps the base quantity of the returns of one original line from adding up to more than the original line's base quantity (plan D13 and the PL3.16 review). A return line is in the original line's own unit, so
/// its entered quantity is exact; its base quantity is that quantity converted and rounded to four decimals, and a factor with a long tail (0.666667) makes the rounded parts add up to a little more than the whole.
/// The rule: the return line that brings the returned ENTERED quantity up to the original line's takes the base quantity that is left (original less what Posted returns and the lines before it took); any other line
/// takes at most what is left. Applied under the original's lock when a return posts, and at save so the draft shows the figure it will post with.
/// </summary>
public static class ReturnBaseAlignment
{
    /// <summary>Re-bases the lines of one return, in order. Lines that name a position the original does not have, or that carry none, are left alone (the caller refuses them).</summary>
    public static void Apply(IReadOnlyList<(int LineNo, StockDocumentLine Line)> originalLines, IReadOnlyDictionary<int, ReturnedQuantity> returned, IEnumerable<StockDocumentLine> returnLines)
    {
        var taken = new Dictionary<int, ReturnedQuantity>();
        foreach (var line in returnLines)
        {
            if (line.ReturnOfLineNo is not { } no || originalLines.FirstOrDefault(o => o.LineNo == no).Line is not { } original) continue;
            var before = taken.TryGetValue(no, out var mine) ? mine : returned.GetValueOrDefault(no);
            var left = Math.Max(original.BaseQty - before.BaseQty, 0m);
            var completes = before.Qty + line.Qty == original.Qty;
            line.RebaseTo(completes ? left : Math.Min(line.BaseQty, left));
            taken[no] = new ReturnedQuantity(before.Qty + line.Qty, before.BaseQty + line.BaseQty);
        }
    }
}

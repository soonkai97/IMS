namespace Ims.Application;

/// <summary>
/// A column of an Excel export or template. A column with a <paramref name="VisibleWith"/> permission is left out of the file for a caller who lacks it.
/// </summary>
public record ReportColumn(string Header, string? VisibleWith = null)
{
    /// <summary>
    /// A column that needs <c>cost.view</c> or <c>valuation.view</c> holds money (the same rule as <c>[VisibleWith]</c> on a property: the permissions exist for cost and
    /// value, and <c>CostVisibilityCoverageTests</c> makes every column that looks like money name one). Its numbers are rounded to 2 decimals and formatted <c>#,##0.00</c>.
    /// </summary>
    public bool IsMoney => VisibleWith is not null;
}

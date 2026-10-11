using Ims.Domain;
namespace Ims.Application;

/// <summary>
/// A column of an Excel export or template. A column with a <paramref name="VisibleWith"/> permission is left out of the file for a caller who lacks it.
/// </summary>
/// <param name="MoneyColumn">Says explicitly whether the column holds money or a unit price. Left out, it follows the permission: the permissions exist for cost and value, so a gated column is one of the two. An explicit value that disagrees with the gating fails <c>CostVisibilityCoverageTests</c>.</param>
/// <param name="UnitPrice">The column holds a DERIVED cost per base unit (a unit price: 4 decimals, <c>#,##0.0000</c>) instead of money (2 decimals, <c>#,##0.00</c>). Only gated columns can be one (checked by an architecture test).</param>
public record ReportColumn(string Header, string? VisibleWith = null, bool? MoneyColumn = null, bool UnitPrice = false)
{
    /// <summary>Money or a unit price: the writer rounds the numbers and formats them.</summary>
    public bool IsMoney => MoneyColumn ?? VisibleWith is not null;

    /// <summary>Decimal places of the numbers in this column: 4 for a unit price, 2 for money; null when the column is not money.</summary>
    public int? DecimalPlaces => !IsMoney ? null : UnitPrice ? Money.UnitPriceDecimals : Money.Decimals;
}

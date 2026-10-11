using Ims.Domain;
namespace Ims.Application;

/// <summary>
/// A column of an Excel export or template. A column with a <paramref name="VisibleWith"/> permission is left out of the file for a caller who lacks it.
/// </summary>
/// <param name="MoneyColumn">Says explicitly whether the column holds money or a unit price. Left out, it follows the permission: the permissions exist for cost and value, so a gated column is one of the two. An explicit value that disagrees with the gating fails <c>CostVisibilityCoverageTests</c>.</param>
public record ReportColumn(string Header, string? VisibleWith = null, bool? MoneyColumn = null)
{
    /// <summary>The columns that hold a DERIVED cost per base unit (a unit price, 4 decimals); every other money column is money (2 decimals).</summary>
    public static readonly IReadOnlySet<string> UnitPriceHeaders = new HashSet<string>(StringComparer.Ordinal) { "Avg cost", "Unit cost" };

    /// <summary>Money or a unit price: the writer rounds the numbers and formats them <c>#,##0.00</c> or <c>#,##0.0000</c>.</summary>
    public bool IsMoney => MoneyColumn ?? VisibleWith is not null;

    /// <summary>Decimal places of the numbers in this column: 4 for a unit price, 2 for money; null when the column is not money.</summary>
    public int? DecimalPlaces => !IsMoney ? null : UnitPriceHeaders.Contains(Header) ? Money.UnitPriceDecimals : Money.Decimals;
}

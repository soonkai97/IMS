using Ims.Domain;
namespace Ims.Application.Features.Accounting;

/// <summary>
/// The one fixed layout of the accounting file (pilot-slice-1-plan section 4): a single sheet, English headers (never translated, because the file is a contract with the accountant's
/// import), these columns in this order. A change of the layout is a new, announced version of the file, never a quiet edit: nothing may reorder, rename or insert a column here.
/// Quantity and Amount carry the sign of the stock effect (a receipt adds, an issue removes, a reversal is the opposite of its posting), so a column sum is the net. The file is at COST:
/// IMS holds no selling price yet. Unit cost (per base unit, 4 decimals) and Amount (the stored line amount, 2 decimals) are gated by <c>cost.view</c> like every cost in an export.
/// </summary>
public static class AccountingLayout
{
    public const string Sheet = "Accounting";

    /// <summary>The first and the last day a period may name (a sane calendar, far from the ends of <see cref="DateOnly"/>).</summary>
    public static readonly DateOnly EarliestDate = new(2000, 1, 1);
    public static readonly DateOnly LatestDate = new(2100, 12, 31);

    public static readonly ReportColumn[] Columns =
    [
        new("Date"), new("Document no."), new("Type"), new("Purpose"), new("Reversal of"), new("Warehouse"), new("Partner"), new("Partner TIN"),
        new("Partner SST reg. no."), new("SKU"), new("Product"), new("HS code"), new("SST code"), new("Unit"), new("Quantity"),
        new("Unit cost", Permissions.CostView, UnitPrice: true), new("Amount", Permissions.CostView)
    ];

    public static string FileName(DateOnly from, DateOnly to) => $"ims-accounting-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.xlsx";
}

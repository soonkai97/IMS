namespace Ims.Application.Features.Accounting;

/// <summary>The one order of the accounting file, oldest first: the instant, the document, the line in entry order, and a posting before the reversal that undoes it. The keys are unique together.</summary>
public static class AccountingOrder
{
    public static IOrderedQueryable<AccountingEntry> Merged(IQueryable<AccountingEntry> rows) =>
        rows.OrderBy(r => r.At).ThenBy(r => r.DocumentId).ThenBy(r => r.LineId).ThenBy(r => r.IsReversal);
}

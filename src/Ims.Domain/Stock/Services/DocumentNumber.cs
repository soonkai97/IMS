namespace Ims.Domain;

public static class DocumentNumber
{
    public static string PrefixFor(DocumentType t) => t switch
    {
        DocumentType.Receipt => DocumentNumberFormat.ReceiptPrefix,
        DocumentType.Issue => DocumentNumberFormat.IssuePrefix,
        DocumentType.Transfer => DocumentNumberFormat.TransferPrefix,
        DocumentType.PurchaseOrder => DocumentNumberFormat.PurchaseOrderPrefix,
        _ => DocumentNumberFormat.AdjustPrefix
    };

    /// <summary>BR-10: times are stored in UTC and shown in Asia/Kuala_Lumpur. Returns yyMM.</summary>
    public static int YearMonthOf(DateTime utcNow)
    {
        var local = utcNow.AddHours(Limits.MalaysiaUtcOffsetHours);
        return local.Year % 100 * 100 + local.Month;
    }

    public static string Format(string prefix, string warehouseCode, int yearMonth, int running) =>
        string.Join(DocumentNumberFormat.Separator, prefix, warehouseCode,
            yearMonth.ToString(DocumentNumberFormat.YearMonthFormat), running.ToString(DocumentNumberFormat.RunningFormat));
}

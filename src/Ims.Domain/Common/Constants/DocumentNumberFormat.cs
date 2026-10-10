namespace Ims.Domain;

/// <summary>Document-number format (BR-11): prefix, warehouse, yyMM, running number.</summary>
public static class DocumentNumberFormat
{
    public const string ReceiptPrefix = "RCV";
    public const string IssuePrefix = "ISS";
    public const string TransferPrefix = "TRF";
    public const string AdjustPrefix = "ADJ";
    public const string Separator = "-";
    public const string YearMonthFormat = "D4";
    public const string RunningFormat = "D4";
}

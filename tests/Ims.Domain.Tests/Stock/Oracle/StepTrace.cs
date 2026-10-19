namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>
/// Everything one step leaves behind, written in the same terms by the engine and by the frozen reference: how it ended (<c>Ok</c>, or the exception type and message),
/// the status of the document, the ledger rows the run produced (none when it failed: the transaction rolls back), the lines of the document and every balance.
/// </summary>
internal sealed record StepTrace(
    string Outcome, string Status, bool OverrideUsed,
    IReadOnlyList<TraceLedger> Ledger, IReadOnlyList<TraceLine> Lines, IReadOnlyList<TraceBalance> Balances)
{
    public const string Ok = "Ok";
    public const string NoDocument = "-";

    public static string Describe(Exception e) => $"{e.GetType().Name}: {e.Message}";
}

namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>
/// One step of a sequence. <paramref name="Doc"/> is the label of the document a Receipt or Issue step creates (and posts), or the document a Reverse step undoes.
/// <paramref name="Reason"/> is the override reason of an <see cref="ApprovalState.Override"/> approval, or the reason of a reversal.
/// The three flags are what the posting run is given: the system setting, and whether the poster may override negative stock.
/// </summary>
internal sealed record OracleStep(
    StepKind Kind, string Doc, IReadOnlyList<OracleLine> Lines, ApprovalState Approval = ApprovalState.NotNeeded,
    bool AllowNegative = false, bool CanOverride = false, string? Reason = null)
{
    public string Op => Kind.ToString();
}

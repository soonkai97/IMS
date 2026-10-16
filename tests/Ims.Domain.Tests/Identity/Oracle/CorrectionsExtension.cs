using static Ims.Domain.Tests.Identity.Oracle.OracleKeys;

namespace Ims.Domain.Tests.Identity.Oracle;

/// <summary>
/// EXTENSION of the oracle for document corrections (B21 recall, B22 cancel on Approved, docs/design/document-corrections.md). The frozen files
/// (<c>document-actions.csv</c> and the others) keep the answers of commit abe1ad0 and are not touched; this one adds the two things that did not
/// exist then, for the same role x status x requiresApproval x isLarge keys and the same six creator/submitter situations:
/// <c>cancel</c> (MayCancel, which now says yes to the reviewer of an Approved document), <c>recall</c> (MayRecall), <c>reviewApproved</c>
/// (MayReviewApproved, one bit: it does not depend on who created or submitted) and <c>actions</c> (AllowedActions, self-approval off, within the
/// limit, with a <c>B</c> for recall in the cell; the letters are those of the old file plus B). The file was written from the code at the commit that
/// added it and checked row by row against the design table; <see cref="CorrectionsOracleTests"/> also proves it differs from the old file in nothing else.
/// </summary>
internal static class CorrectionsExtension
{
    public static readonly OracleTable Table = new(
        "document-corrections.csv", ["role", "status", "requiresApproval", "isLarge"],
        ["cancel", "recall", "reviewApproved", "actions"],
        DocumentKeys,
        k =>
        {
            var (role, status, requiresApproval, isLarge) = ParseDocumentKey(k);
            string Bits(Func<DocumentActionContext, bool> answer) => string.Concat(Contexts.Select(c => Flag(answer(Doc(status, requiresApproval, isLarge, c)))));
            return
            [
                Bits(d => DocumentActionPolicy.MayCancel(role, Me, d)),
                Bits(d => DocumentActionPolicy.MayRecall(role, Me, d)),
                Flag(DocumentActionPolicy.MayReviewApproved(role, Doc(status, requiresApproval, isLarge, Contexts[0]))),
                string.Join(' ', Contexts.Select(c => CellWithRecall(DocumentActionPolicy.AllowedActions(role, Me, Doc(status, requiresApproval, isLarge, c), allowSelfApproval: false, withinApprovalLimit: true))))
            ];
        });

    /// <summary>The old cell (E S A R C P V) plus a B where the document may be taken back to draft.</summary>
    public static string CellWithRecall(IEnumerable<string> actions)
    {
        var list = actions.ToList();
        return Cell(list) + (list.Contains(DocumentActions.Recall) ? 'B' : '.');
    }
}

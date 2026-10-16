using static Ims.Domain.Tests.Identity.Oracle.OracleKeys;

namespace Ims.Domain.Tests.Identity.Oracle;

/// <summary>
/// EXTENSION of the oracle for document corrections (B21 recall, B22 cancel on Approved). The frozen files stay as they are; this one adds what did not
/// exist at abe1ad0, one row per role x status with the four (requiresApproval, isLarge) settings packed as 00/01/10/11 separated by '/':
/// <c>cancel</c> and <c>recall</c> (six bits, the six creator/submitter situations), <c>reviewApproved</c> (one bit) and <c>actions</c> (six cells,
/// self-approval off, within the limit; letters E S A R C P V of the old file plus B = recall). Written from the code that added B21/B22, checked
/// against the design table; <see cref="CorrectionsOracleTests"/> proves it differs from the frozen file in nothing else.
/// </summary>
internal static class CorrectionsExtension
{
    private static readonly (bool RequiresApproval, bool IsLarge)[] Settings = [(false, false), (false, true), (true, false), (true, true)];

    public static readonly OracleTable Table = new(
        "document-corrections.csv", ["role", "status"], ["cancel", "recall", "reviewApproved", "actions"],
        () => from role in SystemRoles from status in Enum.GetValues<DocumentStatus>() select new[] { role, status.ToString() },
        k =>
        {
            var (role, status) = (k[0], Enum.Parse<DocumentStatus>(k[1]));
            string Each(Func<bool, bool, string> answer) => string.Join('/', Settings.Select(s => answer(s.RequiresApproval, s.IsLarge)));
            string Bits(bool ra, bool large, Func<DocumentActionContext, bool> answer) => string.Concat(Contexts.Select(c => Flag(answer(Doc(status, ra, large, c)))));
            return
            [
                Each((ra, l) => Bits(ra, l, d => DocumentActionPolicy.MayCancel(role, Me, d))),
                Each((ra, l) => Bits(ra, l, d => DocumentActionPolicy.MayRecall(role, Me, d))),
                Each((ra, l) => Flag(DocumentActionPolicy.MayReviewApproved(role, Doc(status, ra, l, Contexts[0])))),
                Each((ra, l) => string.Join(' ', Contexts.Select(c =>
                {
                    var actions = DocumentActionPolicy.AllowedActions(role, Me, Doc(status, ra, l, c), allowSelfApproval: false, withinApprovalLimit: true);
                    return Cell(actions) + (actions.Contains(DocumentActions.Recall) ? 'B' : '.');
                })))
            ];
        });
}

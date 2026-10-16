using static Ims.Domain.Tests.Identity.Oracle.OracleKeys;

namespace Ims.Domain.Tests.Identity.Oracle;

/// <summary>
/// The oracle tables as the code answered them before the roles work: every answer is asked of the role-keyed functions
/// (<c>Permissions.For</c>, <c>DocumentActionPolicy</c>, <c>ApprovalPolicy</c>, <c>PostingAuthority</c>, <c>Roles.SeesAllWarehouses</c>).
/// The committed CSV files were written by this class at commit abe1ad0.
/// </summary>
internal static class LegacyBehaviour
{
    public static readonly OracleTable RolesTable = new(
        "roles.csv", ["role"],
        ["permissions", "seesAllWarehouses", "restrictedToOwn", "rank", "reviewsStandard", "reviewsLarge", "postsApprovalRequired", "overridesNegativeStock"],
        RoleKeys,
        k =>
        [
            string.Join(';', Permissions.For(k[0]).Order(StringComparer.Ordinal)),
            Flag(Roles.SeesAllWarehouses(k[0])),
            Flag(DocumentActionPolicy.IsRestrictedToOwn(k[0])),
            ApprovalPolicy.RankOf(k[0]).ToString(),
            Flag(ApprovalPolicy.MayReview(k[0], isLarge: false)),
            Flag(ApprovalPolicy.MayReview(k[0], isLarge: true)),
            Flag(PostingAuthority.MayPostApprovalRequiredDocument(k[0])),
            Flag(PostingAuthority.MayOverrideNegativeStock(k[0])),
        ]);

    public static readonly OracleTable RuleSetsTable = new(
        "rule-sets.csv", ["rules", "amount"],
        ["requiresApproval", "isLarge", .. SystemRoles.Select(r => $"canApprove.{r}")],
        RuleSetKeys,
        k =>
        {
            var rules = ParseRules(k[0]);
            var amount = decimal.Parse(k[1], System.Globalization.CultureInfo.InvariantCulture);
            return
            [
                Flag(ApprovalPolicy.RequiresApproval(rules, DocumentType.Receipt, 1m, amount)),
                Flag(ApprovalPolicy.IsLarge(rules, DocumentType.Receipt, 1m, amount)),
                .. SystemRoles.Select(r => Flag(ApprovalPolicy.CanApprove(r, rules, DocumentType.Receipt, 1m, amount)))
            ];
        });

    public static readonly OracleTable DocumentActionsTable = new(
        "document-actions.csv", ["role", "status", "requiresApproval", "isLarge"],
        ["editSubmit", "cancel", "reviewSelfOff", "reviewSelfOn", "actionsSelfOff", "actionsSelfOffOverLimit", "actionsSelfOn", "actionsSelfOnOverLimit"],
        DocumentKeys,
        k =>
        {
            var (role, status, requiresApproval, isLarge) = ParseDocumentKey(k);
            string Bits(Func<(int Created, int? Submitted), bool> answer) => string.Concat(Contexts.Select(c => Flag(answer(c))));
            string Cells(bool allowSelf, bool withinLimit) =>
                string.Join(' ', Contexts.Select(c => Cell(DocumentActionPolicy.AllowedActions(role, Me, Doc(status, requiresApproval, isLarge, c), allowSelf, withinLimit))));
            return
            [
                Bits(c => DocumentActionPolicy.MayEditOrSubmit(role, Me, Doc(status, requiresApproval, isLarge, c))),
                Bits(c => DocumentActionPolicy.MayCancel(role, Me, Doc(status, requiresApproval, isLarge, c))),
                Bits(c => DocumentActionPolicy.MayReview(role, Me, Doc(status, requiresApproval, isLarge, c), allowSelfApproval: false)),
                Bits(c => DocumentActionPolicy.MayReview(role, Me, Doc(status, requiresApproval, isLarge, c), allowSelfApproval: true)),
                Cells(allowSelf: false, withinLimit: true),
                Cells(allowSelf: false, withinLimit: false),
                Cells(allowSelf: true, withinLimit: true),
                Cells(allowSelf: true, withinLimit: false),
            ];
        });

    public static readonly OracleTable[] All = [RolesTable, RuleSetsTable, DocumentActionsTable];

    /// <summary>
    /// The ONE deliberate departure from the frozen document-actions.csv: document corrections (B22) let the reviewer an Approved document is routed
    /// to cancel it, so on <c>Approved</c> rows the <c>cancel</c> bits and the <c>C</c> letter of the action cells are no longer the old answer (the old
    /// file says nobody could). Those two things are taken out of the comparison here and are pinned by the extension fixture
    /// <c>document-corrections.csv</c> instead. Everything else in those rows (edit, submit, review, post, reverse) and every other row still has to
    /// equal the committed file. The recall action is invisible to the old file: its cells have no letter for it.
    /// </summary>
    public static string WithoutCorrections(string[] key, string column, string value) =>
        key[1] != nameof(DocumentStatus.Approved) ? value
        : column == "cancel" ? ""
        : column.StartsWith("actions", StringComparison.Ordinal) ? value.Replace('C', '.')
        : value;
}

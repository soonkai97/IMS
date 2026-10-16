using static Ims.Domain.Tests.Identity.Oracle.OracleKeys;

namespace Ims.Domain.Tests.Identity.Oracle;

/// <summary>
/// The answers of the Domain policies that take <see cref="ActorRights"/> (RP-1), asked of the rights a system role holds by default
/// (<see cref="SystemRoleDefaults.RightsFor"/>). They must reproduce the oracle written by the role-keyed code.
/// </summary>
internal static class NewBehaviour
{
    public static IReadOnlyDictionary<string, string> Role(IReadOnlyDictionary<string, string> key)
    {
        var rights = SystemRoleDefaults.RightsFor(key["role"], Me);
        return new Dictionary<string, string>
        {
            ["restrictedToOwn"] = Flag(DocumentActionPolicy.IsRestrictedToOwn(rights)),
            ["reviewsStandard"] = Flag(ApprovalPolicy.MayReview(rights, isLarge: false)),
            ["reviewsLarge"] = Flag(ApprovalPolicy.MayReview(rights, isLarge: true)),
        };
    }

    /// <summary>
    /// <see cref="Document"/> as the FROZEN document-actions.csv can judge it. The one deliberate departure from the old answers is document corrections
    /// (B22): the reviewer an Approved document is routed to may now cancel it, so on <c>Approved</c> rows the <c>cancel</c> bits and the <c>C</c> letter of
    /// the action cells are left out here; <c>document-corrections.csv</c> pins them (<see cref="CorrectionsExtension"/>). Everything else, every other
    /// row and the recall action (the old cells have no letter for it) still has to equal the frozen file.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DocumentAsFrozen(IReadOnlyDictionary<string, string> key)
    {
        var answers = Document(key).ToDictionary(a => a.Key, a => a.Value);
        if (key["status"] != nameof(DocumentStatus.Approved)) return answers;
        answers.Remove("cancel");
        foreach (var column in answers.Keys.Where(c => c.StartsWith("actions", StringComparison.Ordinal)).ToList()) answers[column] = answers[column].Replace('C', '.');
        return answers;
    }

    public static IReadOnlyDictionary<string, string> Document(IReadOnlyDictionary<string, string> key)
    {
        var rights = SystemRoleDefaults.RightsFor(key["role"], Me);
        var status = Enum.Parse<DocumentStatus>(key["status"]);
        var requiresApproval = key["requiresApproval"] == "1";
        var isLarge = key["isLarge"] == "1";

        DocumentActionContext DocOf((int Created, int? Submitted) who) => Doc(status, requiresApproval, isLarge, who);
        string Bits(Func<DocumentActionContext, bool> answer) => string.Concat(Contexts.Select(c => Flag(answer(DocOf(c)))));
        string Cells(bool allowSelf, bool withinLimit) =>
            string.Join(' ', Contexts.Select(c => Cell(DocumentActionPolicy.AllowedActions(rights, DocOf(c), allowSelf, withinLimit))));

        return new Dictionary<string, string>
        {
            ["editSubmit"] = Bits(d => DocumentActionPolicy.MayEditOrSubmit(rights, d)),
            ["cancel"] = Bits(d => DocumentActionPolicy.MayCancel(rights, d)),
            ["reviewSelfOff"] = Bits(d => DocumentActionPolicy.MayReview(rights, d, allowSelfApproval: false)),
            ["reviewSelfOn"] = Bits(d => DocumentActionPolicy.MayReview(rights, d, allowSelfApproval: true)),
            ["actionsSelfOff"] = Cells(allowSelf: false, withinLimit: true),
            ["actionsSelfOffOverLimit"] = Cells(allowSelf: false, withinLimit: false),
            ["actionsSelfOn"] = Cells(allowSelf: true, withinLimit: true),
            ["actionsSelfOnOverLimit"] = Cells(allowSelf: true, withinLimit: false),
        };
    }
}

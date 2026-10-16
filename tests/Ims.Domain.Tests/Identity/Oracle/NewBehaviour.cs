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

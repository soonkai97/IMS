using System.Globalization;
using static Ims.Domain.Tests.Identity.Oracle.OracleKeys;

namespace Ims.Domain.Tests.Identity.Oracle;

/// <summary>
/// The answers of the role-keyed code that still exists (<c>Roles.SeesAllWarehouses</c>, <c>ApprovalPolicy</c> by role name),
/// until slice RP-4b deletes it. The committed CSV files were written from the whole role-keyed code at commit abe1ad0; the permission lists (the 15-code map, deleted
/// in RP-4a) are checked against <see cref="SystemRoleDefaults"/> by <c>RightsParityTests</c>, and the policies
/// that moved to <see cref="ActorRights"/> in RP-1 are answered by <see cref="NewBehaviour"/>.
/// </summary>
internal static class LegacyBehaviour
{
    public static IReadOnlyDictionary<string, string> Role(IReadOnlyDictionary<string, string> key)
    {
        var role = key["role"];
        return new Dictionary<string, string>
        {
            ["seesAllWarehouses"] = Flag(Roles.SeesAllWarehouses(role)),
            ["rank"] = ApprovalPolicy.RankOf(role).ToString(CultureInfo.InvariantCulture),
            ["reviewsStandard"] = Flag(ApprovalPolicy.MayReview(role, isLarge: false)),
            ["reviewsLarge"] = Flag(ApprovalPolicy.MayReview(role, isLarge: true)),
        };
    }

    public static IReadOnlyDictionary<string, string> RuleSet(IReadOnlyDictionary<string, string> key)
    {
        var rules = ParseRules(key["rules"]);
        var amount = decimal.Parse(key["amount"], CultureInfo.InvariantCulture);
        var answers = new Dictionary<string, string>
        {
            ["requiresApproval"] = Flag(ApprovalPolicy.RequiresApproval(rules, DocumentType.Receipt, 1m, amount)),
            ["isLarge"] = Flag(ApprovalPolicy.IsLarge(rules, DocumentType.Receipt, 1m, amount)),
        };
        foreach (var role in SystemRoles)
            answers[$"canApprove.{role}"] = Flag(ApprovalPolicy.CanApprove(role, rules, DocumentType.Receipt, 1m, amount));
        return answers;
    }
}

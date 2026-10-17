using System.Globalization;
using static Ims.Domain.Tests.Identity.Oracle.OracleKeys;

namespace Ims.Domain.Tests.Identity.Oracle;

/// <summary>
/// The role-keyed code that decided approvals and warehouse scope until RP-4b, FROZEN here as test code (the running code is by tier and by the per-person flag; nothing
/// in <c>src/</c> keeps a role name for rights). The committed CSV files were written from the whole role-keyed code at commit abe1ad0; the permission lists (the
/// 15-code map, deleted in RP-4a) are checked against <see cref="SystemRoleDefaults"/> by <c>RightsParityTests</c>, and the policies that moved to
/// <see cref="ActorRights"/> in RP-1 are answered by <see cref="NewBehaviour"/>. Pinning this copy to the files proves the copy is faithful, which is what lets
/// <c>RightsParityTests</c> use it as "before".
/// </summary>
internal static class LegacyBehaviour
{
    /// <summary>Admin and Owner saw every warehouse; the others only their assigned ones.</summary>
    public static bool SeesAllWarehouses(string role) => role is Roles.Admin or Roles.Owner;

    /// <summary>Approval authority. A higher rank may approve anything a lower rank may.</summary>
    public static int RankOf(string role) => role switch
    {
        Roles.Admin => 4,
        Roles.Owner => 3,
        Roles.Supervisor => 2,
        _ => 0
    };

    /// <summary>The Admin reviewed anything, the Owner only large documents, a Supervisor only the rest.</summary>
    public static bool MayReview(string role, bool isLarge) => role switch
    {
        Roles.Admin => true,
        Roles.Owner => isLarge,
        Roles.Supervisor => !isLarge,
        _ => false
    };

    /// <summary>The approver had to outrank every rule that fired (a receipt of quantity 1, amount rules only); with no rule fired, a supervisor sufficed.</summary>
    public static bool CanApprove(string approverRole, IReadOnlyList<(string Role, decimal Threshold)> rules, decimal amount)
    {
        var required = rules.Where(r => amount >= r.Threshold).Select(r => RankOf(r.Role)).DefaultIfEmpty(RankOf(Roles.Supervisor)).Max();
        return RankOf(approverRole) >= required;
    }

    public static IReadOnlyDictionary<string, string> Role(IReadOnlyDictionary<string, string> key)
    {
        var role = key["role"];
        return new Dictionary<string, string>
        {
            ["seesAllWarehouses"] = Flag(SeesAllWarehouses(role)),
            ["rank"] = RankOf(role).ToString(CultureInfo.InvariantCulture),
            ["reviewsStandard"] = Flag(MayReview(role, isLarge: false)),
            ["reviewsLarge"] = Flag(MayReview(role, isLarge: true)),
        };
    }

    public static IReadOnlyDictionary<string, string> RuleSet(IReadOnlyDictionary<string, string> key)
    {
        var parts = ParseRuleParts(key["rules"]);
        var rules = ParseRules(key["rules"]);
        var amount = decimal.Parse(key["amount"], CultureInfo.InvariantCulture);
        var answers = new Dictionary<string, string>
        {
            ["requiresApproval"] = Flag(ApprovalPolicy.RequiresApproval(rules, DocumentType.Receipt, 1m, amount)),
            ["isLarge"] = Flag(ApprovalPolicy.IsLarge(rules, DocumentType.Receipt, 1m, amount)),
        };
        foreach (var role in SystemRoles)
            answers[$"canApprove.{role}"] = Flag(CanApprove(role, parts, amount));
        return answers;
    }
}

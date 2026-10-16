using static Ims.Domain.Tests.Identity.Oracle.OracleKeys;

namespace Ims.Domain.Tests.Identity.Oracle;

/// <summary>The three committed oracle files: their columns and the inputs they cover. Their meaning is written in the comment lines at the top of each.</summary>
internal static class OracleTables
{
    public static readonly OracleTable Roles = new(
        "roles.csv", ["role"],
        ["permissions", "seesAllWarehouses", "restrictedToOwn", "rank", "reviewsStandard", "reviewsLarge", "postsApprovalRequired", "overridesNegativeStock"],
        RoleKeys);

    public static readonly OracleTable RuleSets = new(
        "rule-sets.csv", ["rules", "amount"],
        ["requiresApproval", "isLarge", .. SystemRoles.Select(r => $"canApprove.{r}")],
        RuleSetKeys);

    public static readonly OracleTable DocumentActions = new(
        "document-actions.csv", ["role", "status", "requiresApproval", "isLarge"],
        ["editSubmit", "cancel", "reviewSelfOff", "reviewSelfOn", "actionsSelfOff", "actionsSelfOffOverLimit", "actionsSelfOn", "actionsSelfOnOverLimit"],
        DocumentKeys);

    public static readonly OracleTable[] All = [Roles, RuleSets, DocumentActions];
}

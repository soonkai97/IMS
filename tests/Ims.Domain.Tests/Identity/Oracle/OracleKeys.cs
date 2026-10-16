using System.Globalization;

namespace Ims.Domain.Tests.Identity.Oracle;

/// <summary>The inputs of the oracle fixtures and how their answers are written down, shared by every implementation that is checked against them.</summary>
internal static class OracleKeys
{
    public const int Me = 42;
    public const int Other = 43;

    public const decimal FirstThreshold = 100m;
    public const decimal SecondThreshold = 1000m;

    /// <summary>The five system roles plus a role the system does not know (it must get nothing).</summary>
    public static readonly string[] RolesWithUnknown = [Roles.Admin, Roles.Owner, Roles.Supervisor, Roles.WarehouseStaff, Roles.Clerk, "Nobody"];

    public static readonly string[] SystemRoles = [.. RolesWithUnknown.Take(5)];

    public static readonly decimal[] Amounts = [50m, 500m, 5000m];

    /// <summary>Who created and who submitted the document, in the order of the six bits / cells of document-actions.csv.</summary>
    public static readonly (int Created, int? Submitted)[] Contexts =
        [(Me, null), (Me, Me), (Me, Other), (Other, null), (Other, Me), (Other, Other)];

    /// <summary>The seven document actions in the fixed order of a cell: E S A R C P V, a dot where the action is missing.</summary>
    private static readonly (string Action, char Letter)[] ActionLetters =
    [
        (DocumentActions.Edit, 'E'), (DocumentActions.Submit, 'S'), (DocumentActions.Approve, 'A'), (DocumentActions.Reject, 'R'),
        (DocumentActions.Cancel, 'C'), (DocumentActions.Post, 'P'), (DocumentActions.Reverse, 'V')
    ];

    public static string Flag(bool value) => value ? "1" : "0";

    public static string Cell(IEnumerable<string> actions)
    {
        var set = actions.ToHashSet();
        return new string(ActionLetters.Select(a => set.Contains(a.Action) ? a.Letter : '.').ToArray());
    }

    public static DocumentActionContext Doc(DocumentStatus status, bool requiresApproval, bool isLarge, (int Created, int? Submitted) who) =>
        new(status, requiresApproval, isLarge, who.Created, who.Submitted);

    public static IEnumerable<string[]> RoleKeys() => RolesWithUnknown.Select(r => new[] { r });

    /// <summary>Role x status x requiresApproval x isLarge, including the combination no document can be in (large without approval).</summary>
    public static IEnumerable<string[]> DocumentKeys() =>
        from role in SystemRoles
        from status in Enum.GetValues<DocumentStatus>()
        from requiresApproval in new[] { false, true }
        from isLarge in new[] { false, true }
        select new[] { role, status.ToString(), Flag(requiresApproval), Flag(isLarge) };

    public static (string Role, DocumentStatus Status, bool RequiresApproval, bool IsLarge) ParseDocumentKey(string[] key) =>
        (key[0], Enum.Parse<DocumentStatus>(key[1]), key[2] == "1", key[3] == "1");

    /// <summary>No rule, then one rule, then two rules (thresholds 100 and 1000) naming any of the five roles, written "Supervisor@100+Owner@1000".</summary>
    public static IEnumerable<string[]> RuleSetKeys()
    {
        var sets = new List<string> { "-" };
        sets.AddRange(SystemRoles.Select(r => $"{r}@{FirstThreshold}"));
        sets.AddRange(from first in SystemRoles from second in SystemRoles select $"{first}@{FirstThreshold}+{second}@{SecondThreshold}");
        return from set in sets from amount in Amounts select new[] { set, amount.ToString(CultureInfo.InvariantCulture) };
    }

    public static IReadOnlyList<ApprovalRule> ParseRules(string set) =>
        set == "-"
            ? []
            : set.Split('+').Select(part => part.Split('@'))
                .Select(p => ApprovalRule.Create(DocumentType.Receipt, decimal.Parse(p[1], CultureInfo.InvariantCulture), null, p[0])).ToList();
}

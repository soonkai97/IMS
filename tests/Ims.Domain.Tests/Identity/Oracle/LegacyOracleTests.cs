namespace Ims.Domain.Tests.Identity.Oracle;

/// <summary>
/// The role-keyed code that still exists is pinned to the oracle fixtures (<c>*.csv</c> next to this file), so deleting it step by step
/// (RP-4b) cannot move the behaviour of the five system roles unnoticed. The fixtures are frozen: they are never regenerated.
/// </summary>
public class LegacyOracleTests
{
    [Fact]
    public void TheRoleRulesAreAsTheOracleSays()
    {
        var differences = OracleTables.Roles.Differences(LegacyBehaviour.Role);

        Assert.True(differences.Count == 0, Describe(differences));
    }

    [Fact]
    public void TheApprovalRulesAreAsTheOracleSays()
    {
        var differences = OracleTables.RuleSets.Differences(LegacyBehaviour.RuleSet);

        Assert.True(differences.Count == 0, Describe(differences));
    }

    [Fact]
    public void TheFixturesCoverTheWholeInputSpace()
    {
        Assert.Equal(6, OracleTables.Roles.Rows().Count);                                                       // five roles + one unknown
        Assert.Equal(31 * 3, OracleTables.RuleSets.Rows().Count);                                               // none, 5 single, 25 pairs, each at 3 amounts
        Assert.Equal(5 * Enum.GetValues<DocumentStatus>().Length * 2 * 2, OracleTables.DocumentActions.Rows().Count);
    }

    [Fact]
    public void TheFixturesAreNotTrivial_TheAnswersDiffer()
    {
        // Guards the guard: an all-empty or all-identical file would make every comparison pass.
        var permissionLists = OracleTables.Roles.Rows().Select(r => r["permissions"]).Distinct().Count();
        var cells = OracleTables.DocumentActions.Rows()
            .SelectMany(r => new[] { r["actionsSelfOff"], r["actionsSelfOn"] }).SelectMany(c => c.Split(' ')).Distinct().Count();

        Assert.True(permissionLists >= 5, "each role has its own permission list");
        Assert.True(cells >= 8, "the action cells vary");
    }

    private static string Describe(IReadOnlyList<string> differences) =>
        $"{differences.Count} answer(s) differ from the oracle:\n" + string.Join('\n', differences.Take(15));
}

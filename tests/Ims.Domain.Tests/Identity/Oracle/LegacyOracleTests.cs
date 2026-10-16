namespace Ims.Domain.Tests.Identity.Oracle;

/// <summary>
/// RP-0: pins the role-keyed code to the oracle fixtures (<c>*.csv</c> next to this file) before any of it changes. Every later slice of the
/// roles work proves "safe default" against the same files: a failure here means the behaviour of the five system roles moved.
/// </summary>
public class LegacyOracleTests
{
    public static TheoryData<string> Tables()
    {
        var data = new TheoryData<string>();
        foreach (var table in LegacyBehaviour.All) data.Add(table.File);
        return data;
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void TheCodeStillAnswersAsTheCommittedFixtureDoes(string file)
    {
        var table = LegacyBehaviour.All.Single(t => t.File == file);

        var differences = table.Differences(mask: file == LegacyBehaviour.DocumentActionsTable.File ? LegacyBehaviour.WithoutCorrections : null);

        Assert.True(differences.Count == 0, $"{differences.Count} answer(s) differ from the oracle:\n" + string.Join('\n', differences.Take(15)));
    }

    [Fact]
    public void TheFixturesCoverTheWholeInputSpace()
    {
        Assert.Equal(6, LegacyBehaviour.RolesTable.ReadFile().Count - 1);                                      // five roles + unknown
        Assert.Equal(31 * 3, LegacyBehaviour.RuleSetsTable.ReadFile().Count - 1);                              // none, 5 single, 25 pairs, x 3 amounts
        Assert.Equal(5 * Enum.GetValues<DocumentStatus>().Length * 2 * 2, LegacyBehaviour.DocumentActionsTable.ReadFile().Count - 1);
    }

    [Fact]
    public void TheFixturesAreNotTrivial_TheAnswersDiffer()
    {
        // Guards the guard: an all-empty or all-identical file would make every comparison above pass.
        var roles = LegacyBehaviour.RolesTable.ReadFile().Skip(1).Select(l => l.Split(',')[1]).Distinct().Count();
        var cells = LegacyBehaviour.DocumentActionsTable.ReadFile().Skip(1).SelectMany(l => l.Split(',').Skip(8)).SelectMany(c => c.Split(' ')).Distinct().Count();

        Assert.True(roles >= 5, "each role has its own permission list");
        Assert.True(cells >= 8, "the action cells vary");
    }
}

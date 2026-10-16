namespace Ims.Domain.Tests.Identity.Oracle;

/// <summary>
/// Pins the document-corrections extension of the oracle (<see cref="CorrectionsExtension"/>) and proves it adds ONLY recall and cancel-on-Approved to
/// the frozen answers: every frozen row is paired with its setting of the packed extension row.
/// </summary>
public class CorrectionsOracleTests
{
    /// <summary>(frozen row, packed extension row, index of the setting): frozen rows run role, status, requiresApproval 0/1, isLarge 0/1; the packed settings are 00/01/10/11.</summary>
    private static IEnumerable<(IReadOnlyDictionary<string, string> Frozen, IReadOnlyDictionary<string, string> Packed, int Setting)> Pairs()
    {
        var frozen = OracleTables.DocumentActions.Rows();
        var packed = CorrectionsExtension.Table.Rows();
        Assert.Equal(frozen.Count, packed.Count * 4);
        for (var i = 0; i < frozen.Count; i++)
        {
            Assert.Equal((frozen[i]["role"], frozen[i]["status"]), (packed[i / 4]["role"], packed[i / 4]["status"]));
            Assert.Equal(frozen[i]["requiresApproval"] + frozen[i]["isLarge"], new[] { "00", "01", "10", "11" }[i % 4]);
            yield return (frozen[i], packed[i / 4], i % 4);
        }
    }

    private static string Part(IReadOnlyDictionary<string, string> packed, string column, int setting) => packed[column].Split('/')[setting];

    [Fact]
    public void TheCodeStillAnswersAsTheCommittedExtensionDoes()
    {
        var differences = CorrectionsExtension.Table.Differences(CorrectionsExtension.Answers);

        Assert.True(differences.Count == 0, $"{differences.Count} answer(s) differ from the extension:\n" + string.Join('\n', differences.Take(15)));
    }

    [Fact]
    public void CancelDiffersFromTheFrozenAnswerOnApprovedRowsOnly_AndThereIsExactlyTheReviewerRule()
    {
        foreach (var (frozen, packed, setting) in Pairs())
        {
            var now = Part(packed, "cancel", setting);
            if (frozen["status"] != nameof(DocumentStatus.Approved)) Assert.True(frozen["cancel"] == now, $"{frozen["role"]} {frozen["status"]}: cancel moved to {now}");
            // The old guard said yes to every role with the create right (the state machine refused later); now it is the reviewer the document is routed to.
            else Assert.Equal(string.Concat(Enumerable.Repeat(Part(packed, "reviewApproved", setting), 6)), now);
        }
    }

    [Fact]
    public void RecallExistsOnlyOnSubmittedRows_AndOnlyForTheSubmitterWhoMayCreate()
    {
        foreach (var (frozen, packed, setting) in Pairs())
        {
            // contexts: (me,none) (me,me) (me,other) (other,none) (other,me) (other,other): the submitter is me in the 2nd and 5th
            var mayCreate = frozen["role"] != Roles.Owner && frozen["status"] == nameof(DocumentStatus.Submitted);
            Assert.Equal(mayCreate ? "010010" : "000000", Part(packed, "recall", setting));
        }
    }

    [Fact]
    public void TheActionCellsDifferFromTheFrozenOnesOnlyByTheRecallLetterAndByCOnApprovedRows()
    {
        foreach (var (frozen, packed, setting) in Pairs())
        {
            var approved = frozen["status"] == nameof(DocumentStatus.Approved);
            string Same(string cell) => approved ? cell.Replace('C', '.') : cell;
            var oldCells = frozen["actionsSelfOff"].Split(' ').Select(Same);
            var newCells = Part(packed, "actions", setting).Split(' ').Select(c => Same(c)[..7]); // drop the B column
            Assert.True(oldCells.SequenceEqual(newCells), $"{frozen["role"]} {frozen["status"]}: {string.Join(' ', oldCells)} is not {string.Join(' ', newCells)}");
        }
    }
}

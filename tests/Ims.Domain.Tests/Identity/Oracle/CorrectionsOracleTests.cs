namespace Ims.Domain.Tests.Identity.Oracle;

/// <summary>
/// Pins the document-corrections extension of the oracle (<see cref="CorrectionsExtension"/>) and proves it adds ONLY recall and cancel-on-Approved to
/// the frozen answers: the old file, read through the same keys, is the other half of every comparison.
/// </summary>
public class CorrectionsOracleTests
{
    private static string[] Row(OracleTable table, int index) => table.ReadFile()[index + 1].Split(',');

    private static int Column(OracleTable table, string name) => table.KeyColumns.Length + Array.IndexOf(table.ValueColumns, name);

    [Fact]
    public void TheCodeStillAnswersAsTheCommittedExtensionDoes()
    {
        var differences = CorrectionsExtension.Table.Differences();

        Assert.True(differences.Count == 0, $"{differences.Count} answer(s) differ from the extension:\n" + string.Join('\n', differences.Take(15)));
    }

    [Fact]
    public void TheExtensionHasTheSameKeysAsTheFrozenFileInTheSameOrder()
    {
        var old = LegacyBehaviour.DocumentActionsTable;
        var rows = old.ReadFile().Count - 1;

        Assert.Equal(rows, CorrectionsExtension.Table.ReadFile().Count - 1);
        for (var i = 0; i < rows; i++)
            Assert.Equal(Row(old, i).Take(4), Row(CorrectionsExtension.Table, i).Take(4));
    }

    [Fact]
    public void CancelDiffersFromTheFrozenAnswerOnApprovedRowsOnly_AndIsTheReviewerRuleThere()
    {
        var old = LegacyBehaviour.DocumentActionsTable;
        var ext = CorrectionsExtension.Table;
        for (var i = 0; i < old.ReadFile().Count - 1; i++)
        {
            var o = Row(old, i);
            var e = Row(ext, i);
            var cancelOld = o[Column(old, "cancel")];
            var cancelNew = e[Column(ext, "cancel")];
            if (o[1] != nameof(DocumentStatus.Approved))
            {
                Assert.True(cancelOld == cancelNew, $"{string.Join(' ', o.Take(4))}: cancel moved from {cancelOld} to {cancelNew}");
                continue;
            }
            // The old guard said yes to every role with the create right here (the state machine refused it later) and the old action list never offered it;
            // now the guard is exactly the reviewer the document is routed to, whoever created or submitted it.
            Assert.Equal(string.Concat(Enumerable.Repeat(e[Column(ext, "reviewApproved")], 6)), cancelNew); // now exactly the reviewer, whoever created or submitted it
        }
    }

    [Fact]
    public void RecallExistsOnlyOnSubmittedRows_AndOnlyForTheSubmitter()
    {
        var ext = CorrectionsExtension.Table;
        for (var i = 0; i < ext.ReadFile().Count - 1; i++)
        {
            var e = Row(ext, i);
            var recall = e[Column(ext, "recall")];
            if (e[1] != nameof(DocumentStatus.Submitted)) { Assert.Equal("000000", recall); continue; }
            // contexts: (me,none) (me,me) (me,other) (other,none) (other,me) (other,other): the submitter is me in the 2nd and 5th
            var mayCreate = e[0] is Roles.Admin or Roles.Supervisor or Roles.WarehouseStaff or Roles.Clerk;
            Assert.Equal(mayCreate ? "010010" : "000000", recall);
        }
    }

    [Fact]
    public void TheActionCellsDifferFromTheFrozenOnesOnlyByTheRecallLetterAndByCOnApprovedRows()
    {
        var old = LegacyBehaviour.DocumentActionsTable;
        var ext = CorrectionsExtension.Table;
        for (var i = 0; i < old.ReadFile().Count - 1; i++)
        {
            var o = Row(old, i);
            var e = Row(ext, i);
            var approved = o[1] == nameof(DocumentStatus.Approved);
            var oldCells = o[Column(old, "actionsSelfOff")].Split(' ').Select(c => approved ? c.Replace('C', '.') : c);
            var newCells = e[Column(ext, "actions")].Split(' ').Select(c => (approved ? c.Replace('C', '.') : c)[..7]); // drop the B column
            Assert.True(oldCells.SequenceEqual(newCells), $"{string.Join(' ', o.Take(4))}: {string.Join(' ', oldCells)} is not {string.Join(' ', newCells)}");
        }
    }
}

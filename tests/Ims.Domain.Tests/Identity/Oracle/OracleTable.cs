namespace Ims.Domain.Tests.Identity.Oracle;

/// <summary>
/// One oracle fixture: a CSV file (lines starting with # are comments) holding the answers of the code as it was before the roles work, for every
/// combination of its key columns. An implementation answers some of the value columns for a key (<see cref="Differences"/>), so the same file
/// checks the old code and the new types, each for the columns it still can answer.
/// </summary>
internal sealed record OracleTable(string File, string[] KeyColumns, string[] ValueColumns, Func<IEnumerable<string[]>> Keys)
{
    public string Header => string.Join(',', [.. KeyColumns, .. ValueColumns]);

    /// <summary>The rows of the committed file as column to value. Fails when the header or the set of keys is not the expected one, so a dropped row cannot pass.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows()
    {
        var lines = System.IO.File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Identity", "Oracle", File))
            .Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        Assert.Equal(Header, lines[0]);

        var columns = Header.Split(',');
        var rows = lines.Skip(1).Select(l => (IReadOnlyDictionary<string, string>)columns.Zip(l.Split(',')).ToDictionary(p => p.First, p => p.Second)).ToList();
        Assert.Equal(Keys().Select(k => string.Join(',', k)), rows.Select(r => string.Join(',', KeyColumns.Select(c => r[c]))));
        return rows;
    }

    /// <summary>
    /// Every answer that differs from the committed file, as "file:line  key  column  expected  got" (empty when they agree). <paramref name="answers"/>
    /// gets the key columns of a row and returns the value columns it can answer.
    /// </summary>
    public IReadOnlyList<string> Differences(Func<IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> answers)
    {
        var found = new List<string>();
        var rows = Rows();
        for (var i = 0; i < rows.Count; i++)
        {
            foreach (var (column, got) in answers(rows[i]))
            {
                var want = rows[i][column];
                if (want != got) found.Add($"{File} row {i + 1}  {string.Join(' ', KeyColumns.Select(c => rows[i][c]))}  {column}  expected '{want}'  got '{got}'");
            }
        }
        return found;
    }
}

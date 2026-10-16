namespace Ims.Domain.Tests.Identity.Oracle;

/// <summary>
/// One oracle fixture: a CSV file (lines starting with # are comments) holding the answers of the code as it was before the roles work,
/// for every combination of its key columns. <see cref="Compute"/> asks an implementation for the value columns of a key, so the same
/// table checks the old code (RP-0) and the new code (RP-1 onwards) against the one committed file.
/// </summary>
internal sealed record OracleTable(string File, string[] KeyColumns, string[] ValueColumns, Func<IEnumerable<string[]>> Keys, Func<string[], string[]> Compute)
{
    public string Header => string.Join(',', [.. KeyColumns, .. ValueColumns]);

    /// <summary>The data lines of the committed file: header first, comments dropped.</summary>
    public IReadOnlyList<string> ReadFile() =>
        System.IO.File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Identity", "Oracle", File))
            .Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();

    /// <summary>The lines <see cref="Compute"/> produces, in the order of <see cref="Keys"/> (what the generator wrote).</summary>
    public IEnumerable<string> Render() => Keys().Select(k => string.Join(',', [.. k, .. Compute(k)]));

    /// <summary>
    /// Every answer that differs from the committed file, as "file:line  key  column  expected  got" (empty when they agree).
    /// <paramref name="onlyColumns"/> limits the check to some value columns.
    /// </summary>
    public IReadOnlyList<string> Differences(IReadOnlyCollection<string>? onlyColumns = null)
    {
        var committed = ReadFile();
        var found = new List<string>();
        if (committed[0] != Header) found.Add($"{File}: header is '{committed[0]}', expected '{Header}'");

        var keys = Keys().ToList();
        if (committed.Count - 1 != keys.Count) found.Add($"{File}: {committed.Count - 1} rows, expected {keys.Count}");

        for (var i = 0; i < Math.Min(keys.Count, committed.Count - 1); i++)
        {
            var expected = committed[i + 1].Split(',');
            if (!expected.Take(KeyColumns.Length).SequenceEqual(keys[i]))
            {
                found.Add($"{File}:{i + 2}  key '{string.Join(',', expected.Take(KeyColumns.Length))}' is not '{string.Join(',', keys[i])}'");
                continue;
            }

            var actual = Compute(keys[i]);
            for (var c = 0; c < ValueColumns.Length; c++)
            {
                if (onlyColumns is not null && !onlyColumns.Contains(ValueColumns[c])) continue;
                var want = expected[KeyColumns.Length + c];
                if (want != actual[c]) found.Add($"{File}:{i + 2}  {string.Join(' ', keys[i])}  {ValueColumns[c]}  expected '{want}'  got '{actual[c]}'");
            }
        }
        return found;
    }
}

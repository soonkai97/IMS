using System.Text;

namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>
/// One committed fixture: a CSV (lines starting with # are comments) with the answers of the engine for a set of runs. <see cref="Rows"/> turns the runs of any implementation
/// into the same rows, so the engine, the frozen reference and the file can be compared line by line.
/// </summary>
internal sealed record StockOracleFile(string File, string[] Comments, string[] Columns, Func<OracleRun, IEnumerable<string[]>> Rows)
{
    public const string Folder = "Oracle";

    public string Header => string.Join(',', Columns);

    /// <summary>The header and one line per row of every run.</summary>
    public IReadOnlyList<string> Lines(IEnumerable<OracleRun> runs) =>
        [Header, .. runs.SelectMany(Rows).Select(OracleCsv.Row)];

    /// <summary>The data lines of the committed file (comments and blank lines left out).</summary>
    public IReadOnlyList<string> Committed() =>
        System.IO.File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Stock", Folder, File)).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();

    /// <summary>Every line that differs from the committed file, as "file line n: expected ... got ..." (empty when they agree, including in length).</summary>
    public IReadOnlyList<string> Differences(IEnumerable<OracleRun> runs) => Compare(File, Committed(), Lines(runs));

    public static IReadOnlyList<string> Compare(string name, IReadOnlyList<string> expected, IReadOnlyList<string> got)
    {
        var found = new List<string>();
        for (var i = 0; i < Math.Max(expected.Count, got.Count); i++)
        {
            var want = i < expected.Count ? expected[i] : "(no such line)";
            var have = i < got.Count ? got[i] : "(no such line)";
            if (want != have) found.Add($"{name} line {i + 1}: expected '{want}' got '{have}'");
        }
        return found;
    }

    /// <summary>Writes the file (LF line ends, no byte-order mark) into <paramref name="directory"/>: the generator of the fixtures, run once at the freeze.</summary>
    public void Write(string directory, IEnumerable<OracleRun> runs) =>
        System.IO.File.WriteAllText(Path.Combine(directory, File), string.Join('\n', [.. Comments.Select(c => "# " + c), .. Lines(runs)]) + "\n", new UTF8Encoding(false));
}

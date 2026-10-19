namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>
/// The generator of the fixtures. It does nothing in a normal run. It was run once, at the commit before the lot work (main at c89b32b), with
/// <c>IMS_ORACLE_WRITE=&lt;a folder&gt;</c>, and the files it wrote were committed next to this file. Do not run it again to make a failing oracle pass: the files are the
/// answers of the engine as it was.
/// </summary>
public class OracleGeneratorTests
{
    public const string DirectoryVariable = "IMS_ORACLE_WRITE";

    [Fact]
    public void WritesTheFixturesOnlyWhenAFolderIsNamed()
    {
        var directory = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        var scripted = OracleRuns.Scripted(EngineRunner.Run);
        foreach (var file in StockOracleFiles.Scripted) file.Write(directory, scripted);
        StockOracleFiles.Digests.Write(directory, OracleRuns.CommittedSequences(EngineRunner.Run));
    }
}

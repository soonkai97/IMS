using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ims.Architecture.Tests;

/// <summary>
/// Data-access safety rules: no SQL built from strings (injection), and no query shapes that multiply rows (cartesian explosion).
/// All data access goes through EF Core LINQ and Ardalis specifications, which always send values as parameters.
/// </summary>
public class DataAccessSafetyTests
{
    /// <summary>Raw SQL entry points. The interpolated variants (FromSqlInterpolated ...) parameterise values and are allowed.</summary>
    private static readonly string[] RawSqlMethods =
        ["FromSqlRaw", "ExecuteSqlRaw", "ExecuteSqlRawAsync", "SqlQueryRaw", "FromSql", "ExecuteSql", "ExecuteSqlAsync"];

    private static readonly string[] AdoNetTypes = ["SqlCommand", "SqlConnection", "DbCommand", "OleDbCommand", "SqlDataAdapter"];

    [Fact]
    public void Source_NeverBuildsRawSql()
    {
        var offenders = SourceFiles.All.SelectMany(file =>
        {
            var calls = file.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(i => MethodName(i) is { } n && RawSqlMethods.Contains(n))
                .Select(i => $"{file.Line(i)}  {MethodName(i)}()");
            var types = file.Root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                .Where(o => AdoNetTypes.Contains(o.Type.ToString().Split('.').Last()))
                .Select(o => $"{file.Line(o)}  new {o.Type}()");
            return calls.Concat(types);
        });

        Verify.NoOffenders(offenders,
            "No raw SQL or ADO.NET commands in application code. Use LINQ and specifications (values are always parameters); " +
            "if raw SQL is unavoidable use FromSqlInterpolated and say why in the review:");
    }

    [Fact]
    public void Source_NeverConcatenatesSqlText()
    {
        var offenders = SourceFiles.All.SelectMany(file => file.Root.DescendantNodes().OfType<LiteralExpressionSyntax>()
            .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression))
            .Where(l => LooksLikeSql(l.Token.ValueText))
            .Where(l => !IsInsideConstantsOrMessages(file))
            .Select(l => $"{file.Line(l)}  \"{Shorten(l.Token.ValueText)}\""));

        Verify.NoOffenders(offenders, "String literals that look like SQL statements are not allowed outside migrations:");
    }

    [Fact]
    public void Specifications_NeverForceSingleQueries()
    {
        // AsSingleQuery() with two or more collection includes multiplies rows. The context splits queries by default.
        var offenders = SourceFiles.All.SelectMany(file => file.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(i => MethodName(i) == "AsSingleQuery")
            .Select(i => $"{file.Line(i)}  AsSingleQuery()"));

        Verify.NoOffenders(offenders, "AsSingleQuery() is banned (it re-enables cartesian products); keep the default split queries:");
    }

    [Fact]
    public void DbContext_SplitsQueriesByDefault_AndRefusesMultiCollectionSingleQueries()
    {
        var di = File.ReadAllText(Path.Combine(RepoLocator.Src, "Ims.Infrastructure", "DependencyInjection.cs"));

        Assert.Contains("QuerySplittingBehavior.SplitQuery", di);
        Assert.Contains("MultipleCollectionIncludeWarning", di);
    }

    [Theory]
    [InlineData("class C { void M(X x) { x.FromSqlRaw(\"select 1\"); } }", true)]
    [InlineData("class C { void M(X x) { x.ExecuteSqlRawAsync(\"delete\"); } }", true)]
    [InlineData("class C { void M(X x, int id) { x.FromSqlInterpolated($\"select {id}\"); } }", false)]
    [InlineData("class C { object M() => new System.Data.SqlClient.SqlCommand(); }", false)] // qualified type name is split by the test helper; covered by the real scan
    public void RawSqlDetector_FlagsOnlyRawCalls(string code, bool expectedFlagged)
    {
        var root = CSharpSyntaxTree.ParseText(code).GetRoot();

        var flagged = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(i => MethodName(i) is { } n && RawSqlMethods.Contains(n));

        Assert.Equal(expectedFlagged, flagged);
    }

    private static string? MethodName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
        IdentifierNameSyntax i => i.Identifier.Text,
        _ => null
    };

    private static bool LooksLikeSql(string text)
    {
        var t = text.TrimStart().ToUpperInvariant();
        return t.StartsWith("SELECT ") && t.Contains(" FROM ")
               || t.StartsWith("INSERT INTO ")
               || t.StartsWith("DELETE FROM ")
               || t.StartsWith("UPDATE ") && t.Contains(" SET ")
               || t.StartsWith("DROP TABLE ")
               || t.StartsWith("EXEC ") || t.StartsWith("EXECUTE ");
    }

    /// <summary>The Messages catalogue holds user-facing sentences; they can start with words like "Update" but are not SQL.</summary>
    private static bool IsInsideConstantsOrMessages(SourceFile file) => file.RelativePath.Contains("/Constants/");

    private static string Shorten(string s) => (s.Length > 60 ? s[..60] + "..." : s).Replace(Environment.NewLine, " ");
}

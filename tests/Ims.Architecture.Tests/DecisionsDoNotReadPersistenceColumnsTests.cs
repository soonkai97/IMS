using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ims.Architecture.Tests;

/// <summary>
/// <c>RecCreator</c>, <c>RecCreDate</c>, <c>RecRevisor</c> and <c>RecRevDate</c> are audit columns that persistence stamps on save. A decision (who may do what with a document) never reads them
/// directly: the aggregate hands the fact it needs to a Domain type (<c>DocumentOwnership</c> takes who last changed it), so the rule has one home and does not move when a column does
/// (ddd-roadmap section 5, DDD-09). The allow-list names each file that still reads a column, and why; it may only shrink, and a stale entry fails.
/// </summary>
public class DecisionsDoNotReadPersistenceColumnsTests
{
    private static readonly string[] Columns = ["RecCreator", "RecCreDate", "RecRevisor", "RecRevDate"];

    /// <summary>Files (relative to src/) that may read a column, each with the reason.</summary>
    private static readonly (string File, string Reason)[] AllowList =
    [
        ("Ims.Domain/Stock/Models/StockDocument.cs", "hands RecRevisor to DocumentOwnership"),
        ("Ims.Domain/Purchasing/Models/PurchaseOrder.cs", "hands RecRevisor to DocumentOwnership"),
        ("Ims.Domain/Sales/Models/SalesOrder.cs", "hands RecRevisor to DocumentOwnership"),
        ("Ims.Application/Features/Documents/Services/DocumentVersion.cs", "the stale-document answer names who changed the row last and when: a report of the row, not a decision"),
    ];

    private static IEnumerable<SourceFile> Files() => SourceFiles.In("Ims.Domain").Concat(SourceFiles.In("Ims.Application"));

    [Fact]
    public void DecisionsDoNotReadPersistenceColumns()
    {
        var offenders = Files()
            .Where(f => !AllowList.Any(a => a.File == f.RelativePath))
            .SelectMany(f => ColumnReads.In(f.Root).Select(n => $"{f.Line(n)}: {n}"));

        Verify.NoOffenders(offenders, "A decision does not read an audit column (RecCreator, RecCreDate, RecRevisor, RecRevDate); the aggregate hands the fact to a Domain type such as DocumentOwnership (DecisionsDoNotReadPersistenceColumns):");
    }

    [Fact]
    public void EveryAllowListEntry_StillReadsAColumn()
    {
        var reading = Files().Where(f => ColumnReads.In(f.Root).Any()).Select(f => f.RelativePath).ToHashSet();

        Verify.NoOffenders(AllowList.Where(a => !reading.Contains(a.File)).Select(a => $"{a.File} no longer reads a column, so remove its allow-list entry"),
            "A stale allow-list entry hides nothing and lets a read come back unseen:");
    }

    [Fact]
    public void TheRule_LooksAtTheDomainAndTheApplicationLayers()
    {
        Assert.NotEmpty(SourceFiles.In("Ims.Domain"));
        Assert.NotEmpty(SourceFiles.In("Ims.Application"));
    }

    // ---- The detector, over fixture code ----

    [Theory]
    [InlineData("class C { bool M(Doc d, int me) => d.RecRevisor == me; }", "RecRevisor")]
    [InlineData("class C { object M(Row r) => new { r.RecRevDate }; }", "RecRevDate")]
    [InlineData("class C { int? M(Doc d) => d.RecCreator; }", "RecCreator")]
    [InlineData("class C { object M(Doc d) => d?.RecCreDate; }", "RecCreDate")]
    [InlineData("class C { object M(IQueryable<Doc> q) => q.Where(d => d.RecRevisor != null); }", "RecRevisor")]
    public void TheDetector_FindsAColumnBeingRead(string code, string column)
    {
        var found = ColumnReads.In(CSharpSyntaxTree.ParseText(code).GetRoot()).Select(n => n.ToString()).ToList();

        Assert.Equal([column], found);
    }

    [Theory]
    [InlineData("class C { int M(Doc d) => d.CreatedBy; }")]
    [InlineData("class C { int M(Doc d) => d.SubmittedBy ?? 0; }")]
    [InlineData("class C { string M() => \"RecRevisor\"; }")]   // text is not a read
    public void TheDetector_IgnoresEverythingElse(string code)
    {
        Assert.Empty(ColumnReads.In(CSharpSyntaxTree.ParseText(code).GetRoot()));
    }

    private static class ColumnReads
    {
        public static IEnumerable<SyntaxNode> In(SyntaxNode root) =>
            root.DescendantNodes().OfType<IdentifierNameSyntax>().Where(n => Columns.Contains(n.Identifier.Text));
    }
}

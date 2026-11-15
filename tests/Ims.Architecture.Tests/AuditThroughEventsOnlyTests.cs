using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ims.Architecture.Tests;

/// <summary>
/// A business audit row comes from a domain event that a subscriber turns into <c>audit.Record(...)</c> (ddd-roadmap section 2.4, 2.5, 5), not from a call in a handler or an Application service. This is a ratchet:
/// the number of <c>audit.Record(</c> calls in <c>Features/*/UseCases</c> and <c>Features/*/Services</c> is committed and may only go down, one slice at a time. It was 43 after DDD-11b (the 51 calls of the
/// roadmap minus the eight sales-order handler calls that <c>SalesOrderAuditSubscriber</c> took over) and is 42 since DDD-14, which took the one call of <c>SwitchModuleHandler</c> over with <c>ModuleSwitchAuditSubscriber</c> (the subscribers sit in <c>Features/*/EventHandlers</c> and are not counted). The final allow-list is
/// import, attachment and logout, each of which is not an aggregate transition. A detector over fixture code proves the count is not vacuous.
/// </summary>
public class AuditThroughEventsOnlyTests
{
    /// <summary>The calls that remain. Lower it when a slice moves calls to a subscriber; a count above it is a new call that should be an event, a count below it means the slice forgot to lower this number.</summary>
    private const int Baseline = 42;

    private static readonly string[] CountedFolders = ["UseCases", "Services"];

    private static bool IsCounted(SourceFile file) =>
        file.Project == "Ims.Application" && file.Segments is ["Ims.Application", "Features", _, var folder, ..] && CountedFolders.Contains(folder);

    private static List<(string Where, string Text)> Calls() =>
        SourceFiles.All.Where(IsCounted)
            .SelectMany(f => AuditCalls.In(f.Root).Select(c => (Where: f.Line(c), Text: c.ToString().ReplaceLineEndings(" "))))
            .ToList();

    [Fact]
    public void AuditThroughEventsOnly()
    {
        var calls = Calls();

        if (calls.Count > Baseline)
            Verify.NoOffenders(calls.Select(c => c.Where),
                $"There are {calls.Count} audit.Record calls in Features/*/UseCases and Features/*/Services; the baseline is {Baseline}. Raise a domain event on the aggregate and let a subscriber in Features/<Feature>/EventHandlers write the row (AuditThroughEventsOnly):");

        Assert.True(calls.Count == Baseline, $"There are {calls.Count} audit.Record calls, fewer than the baseline {Baseline}: lower Baseline in {nameof(AuditThroughEventsOnlyTests)} so the ratchet keeps the gain.");
    }

    [Fact]
    public void TheRatchet_LooksAtTheFoldersThatHoldHandlersAndServices()
    {
        Assert.Contains(SourceFiles.All, f => IsCounted(f) && f.Segments[3] == "UseCases");
        Assert.Contains(SourceFiles.All, f => IsCounted(f) && f.Segments[3] == "Services");
        Assert.DoesNotContain(SourceFiles.All.Where(IsCounted), f => f.Segments[3] == "EventHandlers");
        Assert.NotEmpty(Calls());
    }

    [Fact]
    public void TheSubscribersOfTheSalesOrder_StageTheirRowsWithTheSameLiteral()
    {
        // The cost-visibility scan reads the text audit.Record(...): a subscriber keeps that literal so its payloads stay scanned.
        var subscriber = SourceFiles.All.Single(f => f.RelativePath == "Ims.Application/Features/Sales/EventHandlers/SalesOrderAuditSubscriber.cs");

        Assert.NotEmpty(AuditCalls.In(subscriber.Root));
    }

    [Fact]
    public void TheSubscriberOfTheModuleSwitch_StagesItsRowWithTheSameLiteral()
    {
        var subscriber = SourceFiles.All.Single(f => f.RelativePath == "Ims.Application/Features/Modules/EventHandlers/ModuleSwitchAuditSubscriber.cs");

        Assert.NotEmpty(AuditCalls.In(subscriber.Root));
    }

    [Fact]
    public void TheModuleSwitchHandler_NoLongerStagesAnAuditRow()
    {
        var handler = SourceFiles.All.Single(f => f.RelativePath == "Ims.Application/Features/Modules/UseCases/SwitchModule/SwitchModuleHandler.cs");

        Assert.Empty(AuditCalls.In(handler.Root));
    }

    [Theory]
    [InlineData("class C { void M(IAuditTrail audit) { audit.Record(\"a\", \"b\", \"c\"); } }", 1)]
    [InlineData("class C { void M(IAuditTrail audit) { audit.Record(\"a\", \"b\", \"c\"); audit.Record(\"d\", \"e\", \"f\", after: new { X = 1 }); } }", 2)]
    [InlineData("class C(IAuditTrail audit) { void M() { if (x) audit.Record(\"a\", \"b\", \"c\"); } }", 1)]
    [InlineData("class C { void M() { this.audit.Record(\"a\", \"b\", \"c\"); } }", 1)]
    [InlineData("class C { void M() { _audit.Record(\"a\", \"b\", \"c\"); } }", 1)]
    [InlineData("class C { void M(IAuditTrail auditTrail) { auditTrail.Record(\"a\", \"b\", \"c\"); } }", 1)]
    public void TheDetector_FindsACallThatStagesAnAuditRow(string code, int expected) =>
        Assert.Equal(expected, AuditCalls.In(CSharpSyntaxTree.ParseText(code).GetRoot()).Count());

    [Theory]
    [InlineData("class C { void M() { var s = \"audit.Record(x)\"; /* audit.Record(x) */ } }")]
    [InlineData("class C { void M(IJournal journal) { journal.Record(\"a\"); } }")]
    [InlineData("class C { void M(IAuditTrail audit) { audit.Flush(); } }")]
    [InlineData("class C { void M() { Record(\"a\"); } }")]
    [InlineData("class C { public void Record(string action) { } }")]
    [InlineData("class C { void M(IRepository<AuditLog> logs) { logs.Add(AuditLog.Record(who, a, t, id, null, null, now)); } }")]   // the row factory of the log itself is not a call that stages an audit event
    public void TheDetector_IgnoresEverythingElse(string code) =>
        Assert.Empty(AuditCalls.In(CSharpSyntaxTree.ParseText(code).GetRoot()));

    private static class AuditCalls
    {
        /// <summary>An invocation of <c>Record</c> on a receiver named <c>audit</c> or <c>auditTrail</c> (with or without a leading underscore or <c>this.</c>): the <c>IAuditTrail</c> a handler holds. <c>AuditLog.Record</c> is the row factory, not a call.</summary>
        public static IEnumerable<InvocationExpressionSyntax> In(SyntaxNode root) =>
            root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(i => i.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Record" } member && ReceiverName(member.Expression).TrimStart('_') is var name
                            && (name.Equals("audit", StringComparison.OrdinalIgnoreCase) || name.Equals("auditTrail", StringComparison.OrdinalIgnoreCase)));

        private static string ReceiverName(ExpressionSyntax receiver) => receiver switch
        {
            IdentifierNameSyntax name => name.Identifier.Text,
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
            _ => ""
        };
    }
}

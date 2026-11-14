using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ims.Architecture.Tests;

/// <summary>
/// A handler orchestrates (load, call the Domain, save, audit); it does not assemble a business rule or policy itself (ENGINEERING-PRACTICES, DDD rule 2; ddd-roadmap section 5).
/// Application code never writes <c>new SomethingRule(...)</c> or <c>new SomethingPolicy(...)</c>: the decision sits behind a method of an aggregate, a value object or a static policy of the Domain.
/// The allow-list names a site that still does it and the slice that removes it; it starts as short as the code allows and may only shrink. A detector over fixture code proves the rule is not vacuous.
/// </summary>
public class HandlersDoNotNewUpDomainRulesTests
{
    /// <summary>
    /// File (relative to src/) and type name of a site that is still allowed, each with the slice that removes it. Empty since DDD-08: the one site the roadmap listed,
    /// <c>SwitchModuleHandler</c> (a reason check), now asks the <c>Reason</c> value object. DDD-14 moves the remaining module decisions into <c>ModuleSwitchPolicy</c>.
    /// </summary>
    private static readonly (string File, string Type)[] AllowList = [];

    [Fact]
    public void HandlersDoNotNewUpDomainRules()
    {
        var offenders = SourceFiles.In("Ims.Application")
            .SelectMany(f => DomainRuleCreations.In(f.Root).Select(c => (File: f.RelativePath, c.Type, Where: f.Line(c.Node))))
            .Where(o => !AllowList.Contains((o.File, o.Type)))
            .Select(o => $"{o.Where}: new {o.Type}(...)");

        Verify.NoOffenders(offenders, "Application code does not build a Domain rule or policy; call a method of the aggregate, a value object or a static policy of the Domain (HandlersDoNotNewUpDomainRules):");
    }

    [Fact]
    public void EveryAllowListEntry_StillMatchesASite()
    {
        var found = SourceFiles.In("Ims.Application")
            .SelectMany(f => DomainRuleCreations.In(f.Root).Select(c => (File: f.RelativePath, c.Type)))
            .ToHashSet();

        Verify.NoOffenders(AllowList.Where(e => !found.Contains(e)).Select(e => $"{e.File}: new {e.Type}(...) is gone, so remove its allow-list entry"),
            "A stale allow-list entry hides nothing and lets the site come back unseen:");
    }

    [Fact]
    public void TheRule_LooksAtTheApplicationLayerOnly()
    {
        Assert.NotEmpty(SourceFiles.In("Ims.Application"));
        Assert.Contains(SourceFiles.In("Ims.Domain"), f => DomainRuleCreations.In(f.Root).Any());   // the Domain builds rules: that is where they belong
    }

    // ---- The detector, over fixture code ----

    [Theory]
    [InlineData("class C { void M() { BusinessRules.Ensure(new ReasonRequiredRule(r, \"m\")); } }", "ReasonRequiredRule")]
    [InlineData("class C { void M() { var p = new ApprovalPolicy(); } }", "ApprovalPolicy")]
    [InlineData("class C { void M() { var p = new Ims.Domain.ApprovalPolicy(); } }", "ApprovalPolicy")]
    [InlineData("class C { void M() { var p = new GenericRule<int>(1); } }", "GenericRule")]
    [InlineData("class C { void M() { var p = new SelfApprovalNotAllowedRule(1, 2, false, \"m\") { }; } }", "SelfApprovalNotAllowedRule")]
    public void TheDetector_FindsARuleOrPolicyBeingBuilt(string code, string type)
    {
        var found = DomainRuleCreations.In(CSharpSyntaxTree.ParseText(code).GetRoot()).Select(c => c.Type).ToList();

        Assert.Equal([type], found);
    }

    [Theory]
    [InlineData("class C { void M() { var r = new Reason(); } }")]
    [InlineData("class C { void M() { var r = Reason.Required(x, \"m\"); } }")]
    [InlineData("class C { void M() { var r = new RuleSetDto(); var p = new PolicyHolder(); } }")]   // the name must END with Rule or Policy
    [InlineData("class C { void M() { var r = new List<Rule>(); } }")]                              // a Rule in a type argument is not a rule being built
    [InlineData("class C { void M() { var r = ApprovalPolicy.ReviewRefusal(1); } }")]               // a static policy is the way
    [InlineData("class C { void M() { var r = new[] { 1 }; Rule x = new(); } }")]                   // target-typed new has no name to read (not detectable by syntax)
    public void TheDetector_IgnoresEverythingElse(string code)
    {
        Assert.Empty(DomainRuleCreations.In(CSharpSyntaxTree.ParseText(code).GetRoot()));
    }

    private static class DomainRuleCreations
    {
        public static IEnumerable<(string Type, SyntaxNode Node)> In(SyntaxNode root) =>
            root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                .Select(c => (Type: NameOf(c.Type), Node: (SyntaxNode)c))
                .Where(c => c.Type.EndsWith("Rule", StringComparison.Ordinal) || c.Type.EndsWith("Policy", StringComparison.Ordinal));

        private static string NameOf(TypeSyntax type) => type switch
        {
            QualifiedNameSyntax q => NameOf(q.Right),
            GenericNameSyntax g => g.Identifier.Text,
            SimpleNameSyntax s => s.Identifier.Text,
            _ => type.ToString(),
        };
    }
}

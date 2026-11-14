using System.Reflection;
using Ims.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ims.Architecture.Tests;

/// <summary>
/// The aggregate roots of the Domain (docs/architecture/ddd-roadmap.md 2.2 and 5). A root keeps the events it raised until the unit of work takes them, and only the root raises them.
/// Every persisted model of the Domain is classified on purpose: a root that already carries events, a root that will (a slice of the roadmap), a small root with no events planned, a child of a root,
/// or an immutable record. A new entity that is on no list fails here, so nobody adds an aggregate by accident. <see cref="RootsToCome"/> only shrinks: a slice moves its roots to <see cref="Roots"/>.
/// </summary>
public class AggregateRootTests
{
    /// <summary>Roots that derive from <c>AggregateRoot</c> or implement <c>IAggregateRoot</c> and raise events.</summary>
    private static readonly string[] Roots = ["SalesOrder"];

    /// <summary>Roots of section 2.2 that raise events in a later slice (DDD-12 to DDD-17 and DDD-24). The slice that adds their events moves each name to <see cref="Roots"/>.</summary>
    private static readonly string[] RootsToCome =
    [
        "PurchaseOrder", "PriceList", "StockDocument", "StockBalance", "DocumentSequence", "SystemSetting", "Role", "User", "ApprovalRule", "Product",
    ];

    /// <summary>Small roots: "no events planned" in section 2.2. They stay plain entities.</summary>
    private static readonly string[] RootsWithoutEvents = ["Warehouse", "Partner", "Category", "Uom", "Attachment", "RefreshToken"];

    /// <summary>Children: reached through their root, never loaded or changed on their own.</summary>
    private static readonly string[] Children =
    [
        "StockDocumentLine", "SalesOrderLine", "PurchaseOrderLine", "PriceListItem", "UomConversion", "ProductBarcode", "RolePermission", "UserWarehouse",
    ];

    /// <summary>Immutable records written by a process: not aggregates.</summary>
    private static readonly string[] Records = ["StockLedgerEntry", "AuditLog"];

    private static IReadOnlyList<Type> PersistedModels { get; } = Layers.TypesOf(Layers.Domain)
        .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && typeof(AuditableEntity).IsAssignableFrom(t))
        .OrderBy(t => t.Name, StringComparer.Ordinal)
        .ToList();

    // ---- The base types ----

    [Fact]
    public void AggregateRoot_IsAnAbstractPublicEntity_ThatKeepsAReadOnlyListOfEvents()
    {
        var type = typeof(AggregateRoot);

        Assert.True(type.IsAbstract);
        Assert.True(typeof(PublicEntity).IsAssignableFrom(type));
        Assert.True(typeof(IAggregateRoot).IsAssignableFrom(type));
        Assert.Same(Layers.Domain, type.Assembly);

        var events = type.GetProperty(nameof(IAggregateRoot.DomainEvents))!;
        Assert.Null(events.SetMethod);   // get-only, so EF does not map it (the same pattern as Lines => _lines)
        Assert.Equal(typeof(IReadOnlyList<IDomainEvent>), events.PropertyType);
    }

    [Fact]
    public void RaisingAnEvent_IsNotPublic_SoOnlyTheRootItselfCanDoIt()
    {
        var raise = typeof(AggregateRoot).GetMethod("Raise", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;

        Assert.NotNull(raise);
        Assert.False(raise.IsPublic);
        Assert.False(raise.IsAssembly);
        Assert.True(raise.IsFamily);
    }

    [Fact]
    public void TheContract_IsTheListAndTheClear_AndNothingElse()
    {
        var members = typeof(IAggregateRoot).GetMembers().Select(m => m.Name).Order().ToList();

        Assert.Equal(new[] { "ClearDomainEvents", "DomainEvents", "get_DomainEvents" }, members);
    }

    // ---- The classification ----

    [Fact]
    public void EveryPersistedModel_IsClassified_ExactlyOnce()
    {
        var all = Roots.Concat(RootsToCome).Concat(RootsWithoutEvents).Concat(Children).Concat(Records).ToList();

        Verify.NoOffenders(PersistedModels.Select(t => t.Name).Except(all), "Persisted models of the Domain on no list (add each to one list in AggregateRootTests, with the roadmap's section 2.2 in hand):");
        Verify.NoOffenders(all.Except(PersistedModels.Select(t => t.Name)), "Names in AggregateRootTests that are no persisted model any more (remove them):");
        Verify.NoOffenders(all.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key), "Names on more than one list:");
    }

    [Fact]
    public void TheRoots_DeriveFromAggregateRoot_OrImplementIAggregateRoot()
    {
        var offenders = PersistedModels.Where(t => Roots.Contains(t.Name) && !typeof(IAggregateRoot).IsAssignableFrom(t)).Select(t => t.Name);

        Verify.NoOffenders(offenders, "Listed in Roots, but not an aggregate root:");
    }

    [Fact]
    public void EveryAggregateRoot_IsOnTheRootsList()
    {
        var offenders = PersistedModels.Where(t => typeof(IAggregateRoot).IsAssignableFrom(t) && !Roots.Contains(t.Name)).Select(t => t.Name);

        Verify.NoOffenders(offenders, "Implements IAggregateRoot but is not in Roots (move it from RootsToCome, or add it):");
    }

    [Fact]
    public void NoOtherModel_IsAnAggregateRoot()
    {
        var offenders = Layers.TypesOf(Layers.Domain)
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IAggregateRoot).IsAssignableFrom(t) && !typeof(AuditableEntity).IsAssignableFrom(t))
            .Select(t => t.Name);

        Verify.NoOffenders(offenders, "An aggregate root is a persisted model:");
    }

    [Fact]
    public void ChildrenAndRecords_AreNeverRoots()
    {
        var offenders = PersistedModels
            .Where(t => (Children.Contains(t.Name) || Records.Contains(t.Name) || RootsWithoutEvents.Contains(t.Name)) && typeof(IAggregateRoot).IsAssignableFrom(t))
            .Select(t => t.Name);

        Verify.NoOffenders(offenders, "A child, a record or a root with no events must not carry the event list:");
    }

    [Fact]
    public void ARootThatRaisesEvents_LeavesTheToComeList()
    {
        var offenders = PersistedModels.Where(t => RootsToCome.Contains(t.Name) && typeof(IAggregateRoot).IsAssignableFrom(t)).Select(t => t.Name);

        Verify.NoOffenders(offenders, "Already a root: move it from RootsToCome to Roots so the list only shrinks:");
    }

    // ---- EventsAreRaisedOnlyByTheirAggregate ----

    [Fact]
    public void EventsAreRaisedOnlyByTheirAggregate()
    {
        var rootNames = Roots.Append(nameof(AggregateRoot)).ToHashSet(StringComparer.Ordinal);

        var raising = SourceFiles.All.Where(f => RaiseRules.Calls(f.Root).Any()).ToList();
        Assert.Contains(raising, f => f.RelativePath == "Ims.Domain/Sales/Models/SalesOrder.cs");   // the rule is not vacuous

        var offenders = raising.Where(f => !RaiseRules.DeclaresOnly(f.Root, rootNames)).Select(f => f.RelativePath);

        Verify.NoOffenders(offenders, "Raise( may appear only in the file of an aggregate root (EventsAreRaisedOnlyByTheirAggregate):");
    }

    [Theory]
    [InlineData("public sealed class SalesOrder : AggregateRoot { void Submit() => Raise(new X()); }", true)]
    [InlineData("public sealed class SalesOrder : AggregateRoot { void Submit() { this.Raise(new X()); } }", true)]
    [InlineData("public sealed class Handler { void Run(SalesOrder o) { o.Raise(new X()); } }", true)]
    [InlineData("public sealed class Handler { void Run() { Raise(new X()); } }", true)]
    [InlineData("public sealed class Handler { void Run() { Raise<X>(); } }", true)]
    [InlineData("public sealed class Handler { void Run() { var raised = Raised(); var x = Raiser(); } }", false)]
    [InlineData("public sealed class Handler { /* Raise(x) */ string s = \"Raise(x)\"; }", false)]
    public void TheRaiseRule_FindsACallOfRaise(string code, bool found)
    {
        Assert.Equal(found, RaiseRules.Calls(CSharpSyntaxTree.ParseText(code).GetRoot()).Any());
    }

    [Theory]
    [InlineData("public sealed class SalesOrder : AggregateRoot { }", true)]
    [InlineData("public sealed class SalesOrder : AggregateRoot { } public sealed class Other { }", false)]
    [InlineData("public sealed class Handler { }", false)]
    public void TheRaiseRule_AcceptsOnlyTheFileOfARoot(string code, bool accepted)
    {
        Assert.Equal(accepted, RaiseRules.DeclaresOnly(CSharpSyntaxTree.ParseText(code).GetRoot(), new HashSet<string> { "SalesOrder" }));
    }
}

/// <summary>Finds calls of <c>Raise</c> in a source file, whoever the receiver is.</summary>
internal static class RaiseRules
{
    public static IEnumerable<InvocationExpressionSyntax> Calls(SyntaxNode root) =>
        root.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(i => NameOf(i.Expression) == "Raise");

    /// <summary>True when every type the file declares is one of the roots (a file of a root declares nothing else that could raise).</summary>
    public static bool DeclaresOnly(SyntaxNode root, IReadOnlySet<string> rootNames)
    {
        var types = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Select(t => t.Identifier.Text).ToList();
        return types.Count > 0 && types.All(rootNames.Contains);
    }

    private static string? NameOf(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        GenericNameSyntax generic => generic.Identifier.Text,
        MemberAccessExpressionSyntax member => NameOf(member.Name),
        _ => null,
    };
}

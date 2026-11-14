using System.Reflection;
using System.Text.RegularExpressions;
using Ims.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ims.Architecture.Tests;

/// <summary>
/// Two guards that come with the first value object (DDD-02, docs/architecture/ddd-roadmap.md section 5):
/// a value object never appears on the wire (the JSON contract stays <c>decimal</c> and <c>string</c>; Application maps), and the primitives that carry money, quantity, cost and factor
/// in the public signatures of the Domain may only go down in number while the value objects take them over (DDD-21 and DDD-22 lower the baseline).
/// Each rule is a pure function that the fixture tests below prove on purpose-built input, so the guard is not vacuous.
/// </summary>
public class ValueObjectUsageTests
{
    /// <summary>
    /// The public Domain members (outside the value objects themselves) that still take or return a <c>decimal</c> named like money, quantity, cost, price, amount, threshold or factor.
    /// Counted on the code of DDD-02 (80 on main before it: the static Money.Amount and Money.RoundUnitPrice left the count when Money became a value object). It may only fall: lower it in the slice that moves a signature to a value object, and never raise it (take the value object instead).
    /// </summary>
    private const int PrimitiveBaseline = 78;

    // ---- ValueObjectsStayOffTheWire, over the real source ----

    [Fact]
    public void NoValueObject_AppearsInAContractOrInTheApi()
    {
        var names = ValueObjectRules.ImplementersIn(Layers.Domain).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Money", names);   // the first value object; the rule below is not vacuous

        var files = SourceFiles.All.Where(f => IsOnTheWire(f.RelativePath)).ToList();
        Assert.Contains(files, f => f.Project == "Ims.Api");
        Assert.Contains(files, f => f.Project == "Ims.Application");

        var offenders = files.SelectMany(f => WireRules.Offenders(f.Root, names).Select(line => $"{f.RelativePath}:{line}"));

        Verify.NoOffenders(offenders, "A value object never appears on a request or response type or in the Api: the wire stays decimal and string, Application maps (ValueObjectsStayOffTheWire):");
    }

    private static bool IsOnTheWire(string path) =>
        path.StartsWith("Ims.Api/", StringComparison.Ordinal)
        || (path.StartsWith("Ims.Application/Features/", StringComparison.Ordinal) && path.Contains("/Contracts/", StringComparison.Ordinal));

    [Theory]
    [InlineData("public sealed record Line(Guid Id, Money Amount);", "Money")]
    [InlineData("public sealed class Dto { public Money Total { get; init; } }", "Money")]
    [InlineData("public sealed class Dto { public Money? Total { get; init; } }", "Money")]
    [InlineData("public sealed class Dto { public IReadOnlyList<Money> Totals { get; init; } }", "Money")]
    [InlineData("public sealed class Dto { public Dictionary<string, Money?> Totals { get; init; } }", "Money")]
    [InlineData("public sealed class Dto { public Money[] Totals { get; init; } }", "Money")]
    [InlineData("public sealed class C { public Task<ActionResult<Money>> Get() => null!; }", "Money")]
    [InlineData("public sealed class C { public void Set(Money total) { } }", "Money")]
    [InlineData("public sealed class C { private Money _total; }", "Money")]
    [InlineData("public sealed class C { public void Set() { var x = new Money(); } }", "Money")]
    [InlineData("public sealed class C { public void Set(object o) { var x = (Money)o; } }", "Money")]
    [InlineData("public sealed class C { public Type T => typeof(Money); }", "Money")]
    [InlineData("public sealed record Line(Ims.Domain.Money Amount);", "Money")]
    [InlineData("public sealed record Line(decimal Amount, Quantity Qty);", "Quantity")]
    public void TheWireRule_FindsAValueObjectInATypePosition(string code, string name)
    {
        var offenders = WireRules.Offenders(CSharpSyntaxTree.ParseText(code).GetRoot(), new HashSet<string> { "Money", "Quantity" }).ToList();

        Assert.Contains(offenders, o => o.Contains(name, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("public sealed record Line(Guid Id, decimal Amount, string Reason);")]
    [InlineData("public sealed class Dto { public decimal? Total { get; init; } public decimal Money { get; init; } }")]   // a member named like the type is not the type
    [InlineData("public sealed class Dto { public Dto(decimal unitCost) => Show(unitCost, UnitCost); public decimal UnitCost { get; } }")]
    [InlineData("public sealed class C { public decimal Get(Line l) => l.Money + Money; public decimal Money { get; } }")]
    [InlineData("/// <summary>A Money amount.</summary>\npublic sealed record Line(decimal Amount); // Money")]
    [InlineData("public sealed class C { public void Set() { var x = Money.Round(1.234m); } }")]   // calling a static helper puts no value object on a type
    public void TheWireRule_IgnoresNamesThatAreNotATypeReference(string code)
    {
        var offenders = WireRules.Offenders(CSharpSyntaxTree.ParseText(code).GetRoot(), new HashSet<string> { "Money", "UnitCost" }).ToList();

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("Ims.Application/Features/Stock/Contracts/LedgerRow.cs", true)]
    [InlineData("Ims.Application/Features/Stock/Contracts/Nested/Row.cs", true)]
    [InlineData("Ims.Api/Features/Stock/StockController.cs", true)]
    [InlineData("Ims.Application/Features/Stock/UseCases/GetLedger/GetLedgerHandler.cs", false)]
    [InlineData("Ims.Application/Common/ReportColumn.cs", false)]
    [InlineData("Ims.Domain/Common/Models/Money.cs", false)]
    public void TheWireRule_LooksAtContractsAndTheApiOnly(string path, bool onTheWire)
    {
        Assert.Equal(onTheWire, IsOnTheWire(path));
    }

    // ---- PrimitiveRatchet, over the real Domain ----

    [Fact]
    public void ThePrimitivesOfTheDomainSignatures_OnlyFall()
    {
        var found = PrimitiveSignatures.In(Layers.TypesOf(Layers.Domain)).Distinct().Order().ToList();

        if (found.Count > PrimitiveBaseline)
        {
            Verify.NoOffenders(found, $"A public Domain member takes or returns a decimal named like money, quantity, cost, price, amount, threshold or factor ({found.Count}, the baseline is {PrimitiveBaseline}). Take a value object (Money, ...) instead (PrimitiveRatchet):");
        }

        Assert.True(found.Count == PrimitiveBaseline,
            $"The count fell to {found.Count} (baseline {PrimitiveBaseline}): lower PrimitiveBaseline in {nameof(ValueObjectUsageTests)} so it cannot rise again.");
    }

    [Fact]
    public void ThePrimitiveRule_CountsTheMembersThatTakeOrReturnAnAmountAsADecimal()
    {
        var found = PrimitiveSignatures.In([typeof(PrimitiveFixtures.Bad)]).ToList();

        Assert.Contains(found, f => f.StartsWith("Bad.Amount ", StringComparison.Ordinal));                         // a property
        Assert.Contains(found, f => f.StartsWith("Bad.Total(", StringComparison.Ordinal) && f.Contains("decimal unitPrice", StringComparison.Ordinal));   // a decimal parameter named like a price
        Assert.Contains(found, f => f.StartsWith("Bad.CostOf(", StringComparison.Ordinal));                         // a member named like a cost
        Assert.Contains(found, f => f.StartsWith("Bad.Scale(", StringComparison.Ordinal) && f.Contains("decimal factor", StringComparison.Ordinal));
        Assert.Contains(found, f => f.StartsWith("Bad.StaticAmount(", StringComparison.Ordinal));                   // a static member
        Assert.Contains(found, f => f.StartsWith("Bad.NullableAmount ", StringComparison.Ordinal));                       // decimal? too
        Assert.Contains(found, f => f.StartsWith("Bad..ctor(", StringComparison.Ordinal));                          // a constructor parameter
        Assert.Equal(7, found.Count);
    }

    [Fact]
    public void ThePrimitiveRule_SkipsWhatIsNotAPrimitiveSignature()
    {
        var found = PrimitiveSignatures.In([typeof(PrimitiveFixtures.Good), typeof(PrimitiveFixtures.AValueObject), typeof(PrimitiveFixtures.Hidden)]).ToList();

        Assert.Empty(found);
    }

    private static class PrimitiveFixtures
    {
        public sealed class Bad
        {
            public Bad(decimal amount) => Amount = amount;

            public decimal Amount { get; private set; }

            public decimal? NullableAmount { get; }

            public decimal Total(decimal unitPrice) => unitPrice;

            public decimal CostOf(int count) => count;

            public void Scale(decimal factor) { }

            public static decimal StaticAmount() => 1m;
        }

        public sealed class Good
        {
            public decimal Rate { get; }                       // a name that is not one of the six

            public int Amount { get; }                         // not a decimal

            public AValueObject Total(AValueObject unitPrice) => unitPrice;  // the value object

            public decimal Round(decimal value) => value;      // neither the member nor the parameter is named like money

            private decimal PrivateAmount => 1m;

            internal decimal InternalAmount => 1m;

            public const decimal MaxAmount = 1m;               // a constant is not a parameter or a return
        }

        public readonly struct AValueObject : IValueObject
        {
            public AValueObject(decimal amount) => Amount = amount;

            public decimal Amount { get; }
        }

        internal sealed class Hidden
        {
            public decimal Amount { get; set; }
        }
    }
}

/// <summary>Finds the value-object types that a source file mentions as a type (a parameter, property, field, return, generic argument, cast, <c>new</c>, <c>typeof</c>).</summary>
internal static class WireRules
{
    public static IEnumerable<string> Offenders(SyntaxNode root, IReadOnlySet<string> valueObjectNames) =>
        root.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(n => valueObjectNames.Contains(n.Identifier.Text) && IsATypeReference(n))
            .Select(n => $"{n.GetLocation().GetLineSpan().StartLinePosition.Line + 1}  {n.Identifier.Text}");

    private static bool IsATypeReference(IdentifierNameSyntax name)
    {
        SyntaxNode node = name;
        while (node.Parent is NullableTypeSyntax or ArrayTypeSyntax or RefTypeSyntax or TupleElementSyntax or TupleTypeSyntax or TypeArgumentListSyntax or GenericNameSyntax or QualifiedNameSyntax)
        {
            node = node.Parent;
        }

        return node.Parent switch
        {
            ParameterSyntax p => p.Type == node,
            PropertyDeclarationSyntax p => p.Type == node,
            IndexerDeclarationSyntax i => i.Type == node,
            VariableDeclarationSyntax v => v.Type == node,
            MethodDeclarationSyntax m => m.ReturnType == node,
            LocalFunctionStatementSyntax l => l.ReturnType == node,
            DelegateDeclarationSyntax d => d.ReturnType == node,
            ObjectCreationExpressionSyntax o => o.Type == node,
            CastExpressionSyntax c => c.Type == node,
            TypeOfExpressionSyntax or DefaultExpressionSyntax or SimpleBaseTypeSyntax or DeclarationExpressionSyntax => true,
            _ => false,
        };
    }
}

/// <summary>The public Domain signatures that still carry an amount, quantity, cost, price, threshold or factor as a bare <c>decimal</c>.</summary>
internal static class PrimitiveSignatures
{
    private static readonly Regex Named = new("qty|quantity|cost|price|amount|threshold|factor", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly;

    /// <summary>One line per member: <c>Type.Member(decimal parameter, ...)</c> for a method or constructor, <c>Type.Member type</c> for a property. Value objects are the home of the primitive and are skipped.</summary>
    public static IEnumerable<string> In(IEnumerable<Type> types) =>
        types.Where(t => (t.IsPublic || t.IsNestedPublic) && !typeof(IValueObject).IsAssignableFrom(t))
            .SelectMany(t => Properties(t).Concat(Methods(t)));

    private static IEnumerable<string> Properties(Type type) =>
        type.GetProperties(Declared)
            .Where(p => IsDecimal(p.PropertyType) && Named.IsMatch(p.Name) && (p.GetMethod is { IsPublic: true } || p.SetMethod is { IsPublic: true }))
            .Select(p => $"{type.Name}.{p.Name} {Show(p.PropertyType)}");

    private static IEnumerable<string> Methods(Type type)
    {
        var methods = type.GetMethods(Declared).Where(m => !m.IsSpecialName && !m.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)).Cast<MethodBase>();
        var constructors = type.GetConstructors(Declared).Cast<MethodBase>();

        foreach (var member in methods.Concat(constructors))
        {
            var decimals = member.GetParameters().Where(p => IsDecimal(p.ParameterType)).ToList();
            var returnsDecimal = member is MethodInfo m && IsDecimal(m.ReturnType);
            if (decimals.Count == 0 && !returnsDecimal) continue;

            var named = (!member.IsConstructor && Named.IsMatch(member.Name)) || decimals.Any(p => Named.IsMatch(p.Name ?? string.Empty));
            if (!named) continue;

            yield return $"{type.Name}.{member.Name}({string.Join(", ", member.GetParameters().Select(p => $"{Show(p.ParameterType)} {p.Name}"))})";
        }
    }

    private static bool IsDecimal(Type type) => type == typeof(decimal) || Nullable.GetUnderlyingType(type) == typeof(decimal);

    private static string Show(Type type) => type == typeof(decimal) ? "decimal" : IsDecimal(type) ? "decimal?" : type.Name;
}

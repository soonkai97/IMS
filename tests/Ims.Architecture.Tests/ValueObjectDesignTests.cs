using System.Reflection;
using System.Runtime.CompilerServices;
using Ims.Domain;

namespace Ims.Architecture.Tests;

/// <summary>
/// A value object is a concept with rules and no identity: immutable, compared by value. Every type that implements <see cref="IValueObject"/> is a struct or a sealed record, has no setter (public, internal or init),
/// no mutable field, no public mutating <c>void</c> method and no persistence or serialization attribute (the mapping lives in Infrastructure, the wire shape in Application).
/// The rule is checked by <see cref="ValueObjectRules"/>, which the fixture tests below prove on purpose-built bad and good types, so the check is not vacuous while the Domain holds no value object yet.
/// </summary>
public class ValueObjectDesignTests
{
    // ---- The rule over the real Domain assembly (empty until the first value object lands) ----

    [Fact]
    public void EveryValueObjectOfTheDomain_FollowsTheDesignRules()
    {
        var offenders = ValueObjectRules.ImplementersIn(Layers.Domain)
            .SelectMany(t => ValueObjectRules.Violations(t).Select(v => $"{t.Name}: {v}"));

        Verify.NoOffenders(offenders, "A value object is an immutable struct or sealed record (see ValueObjectDesignTests):");
    }

    [Fact]
    public void TheMarker_IsAnEmptyInterface_OfTheDomain()
    {
        Assert.True(typeof(IValueObject).IsInterface);
        Assert.Same(Layers.Domain, typeof(IValueObject).Assembly);
        Assert.Empty(typeof(IValueObject).GetMembers());
        Assert.Empty(typeof(IValueObject).GetInterfaces());
    }

    // ---- The detector, over fixture types ----

    [Fact]
    public void Detector_FindsImplementers_AndOnlyImplementers()
    {
        var found = ValueObjectRules.ImplementersIn(typeof(ValueObjectDesignTests).Assembly).ToList();

        Assert.Contains(typeof(Fixtures.GoodStruct), found);
        Assert.Contains(typeof(Fixtures.NotSealedClass), found);
        Assert.DoesNotContain(typeof(Fixtures.NotAValueObject), found);
        Assert.DoesNotContain(typeof(IValueObject), found);
    }

    [Theory]
    [InlineData(typeof(Fixtures.GoodStruct))]
    [InlineData(typeof(Fixtures.GoodSealedRecord))]
    [InlineData(typeof(Fixtures.GoodStructWithDeconstruct))]
    public void Detector_AcceptsAWellFormedValueObject(Type type)
    {
        Assert.Empty(ValueObjectRules.Violations(type));
    }

    [Theory]
    [InlineData(typeof(Fixtures.NotSealedClass), "not a struct or a sealed record")]
    [InlineData(typeof(Fixtures.SealedPlainClass), "not a struct or a sealed record")]
    [InlineData(typeof(Fixtures.NotSealedRecord), "not a struct or a sealed record")]
    [InlineData(typeof(Fixtures.PublicSetter), "setter")]
    [InlineData(typeof(Fixtures.InternalSetter), "setter")]
    [InlineData(typeof(Fixtures.InitSetter), "setter")]
    [InlineData(typeof(Fixtures.MutableField), "mutable field")]
    [InlineData(typeof(Fixtures.PrivateSetterBackingField), "mutable field")]
    [InlineData(typeof(Fixtures.MutatingVoidMethod), "mutating void method")]
    [InlineData(typeof(Fixtures.EfAttribute), "attribute")]
    [InlineData(typeof(Fixtures.SerializationAttributeOnMember), "attribute")]
    public void Detector_FlagsEachBreach(Type type, string expected)
    {
        var violations = ValueObjectRules.Violations(type);

        Assert.Contains(violations, v => v.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Detector_NamesTheOffendingMember()
    {
        Assert.Contains(ValueObjectRules.Violations(typeof(Fixtures.PublicSetter)), v => v.Contains("Amount", StringComparison.Ordinal));
        Assert.Contains(ValueObjectRules.Violations(typeof(Fixtures.MutableField)), v => v.Contains("Scale", StringComparison.Ordinal));
        Assert.Contains(ValueObjectRules.Violations(typeof(Fixtures.MutatingVoidMethod)), v => v.Contains("Add", StringComparison.Ordinal));
    }

    [Fact]
    public void Detector_FlagsAPositionalRecord_BecauseItsPropertiesAreInit()
    {
        // The rule asks for explicit get-only properties and a private constructor; a positional record would generate init setters (and a Deconstruct, which is not a mutator).
        var violations = ValueObjectRules.Violations(typeof(Fixtures.PositionalRecord));

        Assert.Contains(violations, v => v.Contains("setter", StringComparison.Ordinal) && v.Contains("Amount", StringComparison.Ordinal));
        Assert.DoesNotContain(violations, v => v.Contains("Deconstruct", StringComparison.Ordinal));
    }

    /// <summary>The fixtures. They sit in the test assembly, which the real-assembly rule never scans.</summary>
    private static class Fixtures
    {
        public readonly struct GoodStruct : IValueObject
        {
            public GoodStruct(decimal amount) => Amount = amount;

            public decimal Amount { get; }

            public GoodStruct Plus(GoodStruct other) => new(Amount + other.Amount);
        }

        public sealed record GoodSealedRecord : IValueObject
        {
            private GoodSealedRecord(string text) => Text = text;

            public string Text { get; }

            public static GoodSealedRecord Of(string text) => new(text);
        }

        public readonly struct GoodStructWithDeconstruct : IValueObject
        {
            private readonly decimal _value;

            public GoodStructWithDeconstruct(decimal value) => _value = value;

            public decimal Value => _value;

            public void Deconstruct(out decimal value) => value = _value;
        }

        public class NotSealedClass : IValueObject
        {
            public decimal Amount { get; }
        }

        public sealed class SealedPlainClass : IValueObject
        {
            public decimal Amount { get; }
        }

        public record NotSealedRecord : IValueObject
        {
            public decimal Amount { get; }
        }

        public sealed record PublicSetter : IValueObject
        {
            public decimal Amount { get; set; }
        }

        public sealed record InternalSetter : IValueObject
        {
            public decimal Amount { get; internal set; }
        }

        public sealed record InitSetter : IValueObject
        {
            public decimal Amount { get; init; }
        }

        public sealed record PositionalRecord(decimal Amount) : IValueObject;

        public struct MutableField : IValueObject
        {
            public int Scale;
        }

        public sealed record PrivateSetterBackingField : IValueObject
        {
            public decimal Amount { get; private set; }
        }

        public struct MutatingVoidMethod : IValueObject
        {
            private decimal _total;

            public readonly decimal Total => _total;

            public void Add(decimal amount) => _total += amount;
        }

        [Microsoft.EntityFrameworkCore.Owned]
        public sealed record EfAttribute : IValueObject
        {
            public decimal Amount { get; }
        }

        public sealed record SerializationAttributeOnMember : IValueObject
        {
            [System.Text.Json.Serialization.JsonPropertyName("amount")]
            public decimal Amount { get; }
        }

        /// <summary>Not marked: the detector must not pick it up even though it breaks every rule.</summary>
        public sealed class NotAValueObject
        {
            public decimal Amount { get; set; }
        }
    }
}

/// <summary>The design rule for value objects, as a pure function over a type so that fixtures can prove it.</summary>
internal static class ValueObjectRules
{
    private const BindingFlags AllDeclared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Namespaces whose attributes mean "persistence" or "serialization": the mapping lives in Infrastructure, the wire shape in Application.</summary>
    private static readonly string[] ForbiddenAttributeNamespaces =
    [
        "Microsoft.EntityFrameworkCore",
        "System.ComponentModel.DataAnnotations",
        "System.Text.Json",
        "System.Runtime.Serialization",
        "System.Xml.Serialization",
        "Newtonsoft.Json",
        "Ardalis",
    ];

    private static readonly string[] ForbiddenAttributeNames = ["SerializableAttribute", "NonSerializedAttribute"];

    /// <summary>Every type of the assembly that implements the marker (the marker itself and other interfaces excluded).</summary>
    public static IEnumerable<Type> ImplementersIn(Assembly assembly) =>
        Layers.TypesOf(assembly).Where(t => t is { IsInterface: false } && typeof(IValueObject).IsAssignableFrom(t));

    /// <summary>The ways this type breaks the design rules; empty when it is a well-formed value object.</summary>
    public static IReadOnlyList<string> Violations(Type type)
    {
        var found = new List<string>();

        if (!(type.IsValueType || (IsRecord(type) && type.IsSealed)))
        {
            found.Add("not a struct or a sealed record");
        }

        foreach (var property in type.GetProperties(AllDeclared).Where(p => p.SetMethod is { IsPublic: true } or { IsAssembly: true } or { IsFamilyOrAssembly: true }))
        {
            found.Add($"{property.Name}: {(IsInit(property.SetMethod!) ? "init" : "public or internal")} setter");
        }

        foreach (var field in type.GetFields(AllDeclared).Where(f => !f.IsLiteral && !f.IsInitOnly))
        {
            found.Add($"{field.Name}: mutable field (make it readonly)");
        }

        foreach (var method in type.GetMethods(AllDeclared).Where(IsPublicMutatingVoid))
        {
            found.Add($"{method.Name}: public mutating void method (return a new value instead)");
        }

        var members = type.GetMembers(AllDeclared).Append(type);
        foreach (var attribute in members.SelectMany(m => m.GetCustomAttributesData()).Where(IsForbidden))
        {
            found.Add($"{attribute.AttributeType.Name}: persistence or serialization attribute");
        }

        return found;
    }

    /// <summary>A record class carries the compiler's clone method; a plain class does not.</summary>
    private static bool IsRecord(Type type) => type.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null;

    private static bool IsInit(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));

    // Property accessors and operators are special names; the compiler's Deconstruct of a record is not a mutator.
    private static bool IsPublicMutatingVoid(MethodInfo method) =>
        method is { IsPublic: true, IsStatic: false, IsSpecialName: false } && method.ReturnType == typeof(void) && method.Name != "Deconstruct";

    private static bool IsForbidden(CustomAttributeData attribute) =>
        ForbiddenAttributeNames.Contains(attribute.AttributeType.Name)
        || ForbiddenAttributeNamespaces.Any(ns => attribute.AttributeType.Namespace?.StartsWith(ns, StringComparison.Ordinal) == true);
}

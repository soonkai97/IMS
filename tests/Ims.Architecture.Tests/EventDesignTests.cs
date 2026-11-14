using System.Reflection;
using Ims.Domain;

namespace Ims.Architecture.Tests;

/// <summary>
/// A domain event is a fact an aggregate raises about a change that happened: a sealed record of the Domain that carries ids, status before and after, reasons and value objects, never an entity
/// (a subscriber that kept an entity would read it after the unit of work moved on). The name is in the past tense; reflection cannot check a tense, so the names are a committed list that
/// grows on purpose with the slice that adds an event (docs/architecture/ddd-roadmap.md 2.4, section 5). The rule is a pure function that the fixtures below prove, so it is not vacuous.
/// </summary>
public class EventDesignTests
{
    /// <summary>Every domain event of the Domain, by name. A new event goes here in the pull request that adds it; a name that no longer exists goes out.</summary>
    private static readonly string[] ApprovedNames =
    [
        // DDD-06: the sales order
        "SalesOrderCreated", "SalesOrderUpdated", "SalesOrderSubmitted", "SalesOrderApproved", "SalesOrderRejected", "SalesOrderRecalled", "SalesOrderCancelled", "SalesOrderClosed",
    ];

    // ---- The rules over the real assemblies ----

    [Fact]
    public void EveryDomainEvent_FollowsTheDesignRules()
    {
        var offenders = EventRules.ImplementersIn(Layers.Domain)
            .SelectMany(t => EventRules.Violations(t).Select(v => $"{t.Name}: {v}"));

        Verify.NoOffenders(offenders, "A domain event is a sealed record of the Domain that carries facts, never an entity (see EventDesignTests):");
    }

    [Fact]
    public void TheEventsOfTheDomain_AreTheCommittedList()
    {
        var found = EventRules.ImplementersIn(Layers.Domain).Select(t => t.Name).ToList();

        Verify.NoOffenders(found.Except(ApprovedNames), "Domain events that are not on the committed list (add the name to ApprovedNames in EventDesignTests):");
        Verify.NoOffenders(ApprovedNames.Except(found), "Names on the committed list that are no domain event any more (remove them):");
    }

    [Fact]
    public void NoDomainEvent_LivesOutsideTheDomain()
    {
        var offenders = new[] { Layers.Application, Layers.Infrastructure }
            .SelectMany(EventRules.ImplementersIn)
            .Select(t => $"{t.Assembly.GetName().Name}: {t.Name}");

        Verify.NoOffenders(offenders, "An event is a fact of the Domain; a subscriber lives in Application, but its event does not:");
    }

    [Fact]
    public void TheMarker_IsAnEmptyInterface_OfTheDomain()
    {
        Assert.True(typeof(IDomainEvent).IsInterface);
        Assert.Same(Layers.Domain, typeof(IDomainEvent).Assembly);
        Assert.Empty(typeof(IDomainEvent).GetMembers());
        Assert.Empty(typeof(IDomainEvent).GetInterfaces());
    }

    [Fact]
    public void TheRule_IsNotVacuous_TheSalesOrderEventsAreFound()
    {
        var found = EventRules.ImplementersIn(Layers.Domain).Select(t => t.Name).ToList();

        Assert.Contains("SalesOrderSubmitted", found);
        Assert.Equal(ApprovedNames.Length, found.Count);
    }

    // ---- The detector, over fixture types ----

    [Fact]
    public void Detector_FindsImplementers_AndOnlyImplementers()
    {
        var found = EventRules.ImplementersIn(typeof(EventDesignTests).Assembly).ToList();

        Assert.Contains(typeof(Fixtures.GoodEvent), found);
        Assert.Contains(typeof(Fixtures.NotSealedRecord), found);
        Assert.DoesNotContain(typeof(Fixtures.NotAnEvent), found);
        Assert.DoesNotContain(typeof(IDomainEvent), found);
    }

    [Theory]
    [InlineData(typeof(Fixtures.GoodEvent))]
    [InlineData(typeof(Fixtures.GoodEventWithValuesAndIds))]
    public void Detector_AcceptsAWellFormedEvent(Type type)
    {
        Assert.Empty(EventRules.Violations(type));
    }

    [Theory]
    [InlineData(typeof(Fixtures.NotSealedRecord), "not a sealed record")]
    [InlineData(typeof(Fixtures.SealedPlainClass), "not a sealed record")]
    [InlineData(typeof(Fixtures.EventStruct), "not a sealed record")]
    [InlineData(typeof(Fixtures.CarriesAnEntity), "entity")]
    [InlineData(typeof(Fixtures.CarriesAnAggregateRoot), "entity")]
    [InlineData(typeof(Fixtures.CarriesAListOfEntities), "entity")]
    [InlineData(typeof(Fixtures.CarriesANullableEntity), "entity")]
    [InlineData(typeof(Fixtures.CarriesAPersistedModel), "entity")]
    [InlineData(typeof(Fixtures.EntityInAConstructorParameter), "entity")]
    public void Detector_FlagsEachBreach(Type type, string expected)
    {
        Assert.Contains(EventRules.Violations(type), v => v.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Detector_NamesTheOffendingProperty()
    {
        Assert.Contains(EventRules.Violations(typeof(Fixtures.CarriesAnEntity)), v => v.Contains("Order", StringComparison.Ordinal));
    }

    /// <summary>The fixtures. They sit in the test assembly, which the real-assembly rules never scan.</summary>
    private static class Fixtures
    {
        public sealed record GoodEvent(string No, SalesOrderStatus StatusBefore, SalesOrderStatus StatusAfter) : IDomainEvent;

        public sealed record GoodEventWithValuesAndIds(Guid Id, int WarehouseId, Money Amount, string? Reason, IReadOnlyList<int> LineIds) : IDomainEvent;

        public record NotSealedRecord(string No) : IDomainEvent;

        public sealed class SealedPlainClass : IDomainEvent
        {
            public string No { get; } = "";
        }

        public readonly record struct EventStruct(string No) : IDomainEvent;

        public sealed record CarriesAnEntity(SalesOrderLine Order) : IDomainEvent;

        public sealed record CarriesAnAggregateRoot(SalesOrder Order) : IDomainEvent;

        public sealed record CarriesAListOfEntities(IReadOnlyList<SalesOrderLine> Lines) : IDomainEvent;

        public sealed record CarriesANullableEntity(Warehouse? Warehouse) : IDomainEvent;

        public sealed record CarriesAPersistedModel(StockLedgerEntry Entry) : IDomainEvent;

        public sealed record EntityInAConstructorParameter : IDomainEvent
        {
            public EntityInAConstructorParameter(Warehouse warehouse) => WarehouseCode = warehouse.Code;

            public string WarehouseCode { get; }
        }

        public sealed class NotAnEvent
        {
            public SalesOrder Order { get; set; } = null!;
        }
    }
}

/// <summary>The design rule for domain events, as a pure function over a type so that fixtures can prove it.</summary>
internal static class EventRules
{
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Every type of the assembly that implements the marker (the marker itself and other interfaces excluded).</summary>
    public static IEnumerable<Type> ImplementersIn(System.Reflection.Assembly assembly) =>
        Layers.TypesOf(assembly).Where(t => t is { IsInterface: false } && typeof(IDomainEvent).IsAssignableFrom(t));

    /// <summary>The ways this type breaks the design rules; empty when it is a well-formed event.</summary>
    public static IReadOnlyList<string> Violations(Type type)
    {
        var found = new List<string>();

        if (!(type is { IsClass: true, IsSealed: true } && IsRecord(type)))
        {
            found.Add("not a sealed record");
        }

        foreach (var property in type.GetProperties(Declared).Where(p => ContainsAnEntity(p.PropertyType)))
        {
            found.Add($"{property.Name}: carries an entity ({property.PropertyType.Name}); carry its id and the facts instead");
        }

        foreach (var parameter in type.GetConstructors(Declared).SelectMany(c => c.GetParameters()).Where(p => ContainsAnEntity(p.ParameterType)))
        {
            found.Add($"{parameter.Name}: constructor parameter is an entity ({parameter.ParameterType.Name})");
        }

        return found;
    }

    /// <summary>An entity is a persisted row: anything of <see cref="AuditableEntity"/> (an aggregate root included) or a persisted model that is not one (the mark is a private parameterless constructor, as in DomainEncapsulationTests).</summary>
    public static bool IsEntity(Type type) =>
        typeof(AuditableEntity).IsAssignableFrom(type)
        || typeof(IAggregateRoot).IsAssignableFrom(type)
        || (type is { IsClass: true, IsAbstract: false } && !typeof(Exception).IsAssignableFrom(type) && !typeof(IDomainEvent).IsAssignableFrom(type)
            && type.Assembly == typeof(Entity).Assembly && type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes) is not null);

    /// <summary>The type itself, its nullable underlying type, its array element or any generic argument (a list, a dictionary) is an entity.</summary>
    private static bool ContainsAnEntity(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (IsEntity(underlying))
        {
            return true;
        }

        if (underlying.IsArray)
        {
            return ContainsAnEntity(underlying.GetElementType()!);
        }

        return underlying.IsGenericType && underlying.GetGenericArguments().Any(ContainsAnEntity);
    }

    /// <summary>A record class carries the compiler's clone method; a plain class does not.</summary>
    private static bool IsRecord(Type type) => type.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null;
}

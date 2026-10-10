using System.Reflection;
using Ims.Domain;

namespace Ims.Architecture.Tests;

/// <summary>Entities protect their invariants: state changes only through methods, instances only through factories.</summary>
public class DomainEncapsulationTests
{
    /// <summary>Aggregate roots and persisted models that do not derive from <see cref="Entity"/>.</summary>
    private static readonly Type[] ExtraEntities =
    [
        typeof(StockDocument), typeof(StockDocumentLine), typeof(StockBalance), typeof(StockLedgerEntry),
        typeof(DocumentSequence), typeof(SystemSetting), typeof(AuditLog), typeof(UserWarehouse)
    ];

    private static IReadOnlyList<Type> Entities { get; } = Layers.TypesOf(Layers.Domain)
        .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && typeof(Entity).IsAssignableFrom(t))
        .Concat(ExtraEntities)
        .Distinct()
        .ToList();

    [Fact]
    public void Entities_AreDiscovered()
    {
        Assert.Contains(typeof(Product), Entities);
        Assert.Contains(typeof(User), Entities);
        Assert.Contains(typeof(StockDocument), Entities);
    }

    [Fact]
    public void Entities_HaveNoPublicSetters()
    {
        var offenders = Entities
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(p => p.SetMethod is { IsPublic: true })
                .Select(p => $"{t.Name}.{p.Name}"));

        Verify.NoOffenders(offenders, "Domain entities must not expose public (or init) setters; add a behaviour method instead:");
    }

    [Fact]
    public void Entities_HaveNoPublicConstructors()
    {
        var offenders = Entities.Where(t => t.GetConstructors(BindingFlags.Instance | BindingFlags.Public).Length > 0).Select(t => t.Name);

        Verify.NoOffenders(offenders, "Domain entities must be created through a factory method, not a public constructor:");
    }

    [Fact]
    public void Entities_HaveNoPublicFields()
    {
        var offenders = Entities
            .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.Public).Select(f => $"{t.Name}.{f.Name}"));

        Verify.NoOffenders(offenders, "Domain entities must not expose public fields:");
    }

    [Fact]
    public void EveryEntity_OfTheDomain_IsCoveredByTheAboveRules()
    {
        // A new public model that is neither an Entity nor listed in ExtraEntities (and has a private parameterless
        // constructor, the mark of a persisted model) would silently escape the rules above.
        var offenders = Layers.TypesOf(Layers.Domain)
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && !Entities.Contains(t))
            .Where(t => t.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes) is not null)
            .Where(t => !typeof(Exception).IsAssignableFrom(t))
            .Select(t => t.Name);

        Verify.NoOffenders(offenders, "Add these persisted models to ExtraEntities in DomainEncapsulationTests:");
    }
}

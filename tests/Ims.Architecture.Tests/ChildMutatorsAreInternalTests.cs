using System.Reflection;
using Ims.Domain;

namespace Ims.Architecture.Tests;

/// <summary>
/// A child of an aggregate changes only through its root, so a child has no public method that changes it: the root's method calls an internal one (ddd-roadmap section 5, DDD-09). A reader
/// of a child is a property, not a method. The allow-list is empty: <c>StockDocumentLine.RebaseTo</c> (called only by <c>ReturnBaseAlignment</c> in the same assembly) and
/// <c>UomConversion.ChangeFactor</c> (called only by <c>Product</c>) are internal now.
/// </summary>
public class ChildMutatorsAreInternalTests
{
    private static readonly Type[] Children =
    [
        typeof(StockDocumentLine), typeof(SalesOrderLine), typeof(PurchaseOrderLine), typeof(PriceListItem),
        typeof(UomConversion), typeof(ProductBarcode), typeof(RolePermission), typeof(UserWarehouse)
    ];

    /// <summary>Type and method name of a public method that is still allowed, each with the slice that removes it.</summary>
    private static readonly (string Type, string Method)[] AllowList = [];

    private static IEnumerable<(string Type, string Method)> PublicMethods(Type type) =>
        type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)   // property accessors
            .Select(m => (type.Name, m.Name));

    [Fact]
    public void AChildHasNoPublicMethodThatChangesIt()
    {
        var offenders = Children.SelectMany(PublicMethods).Where(m => !AllowList.Contains(m)).Select(m => $"{m.Type}.{m.Method}");

        Verify.NoOffenders(offenders, "A child entity changes only through its aggregate root: make the method internal and call it from the root (ChildMutatorsAreInternal):");
    }

    [Fact]
    public void EveryAllowListEntry_StillExists()
    {
        var found = Children.SelectMany(PublicMethods).ToHashSet();

        Verify.NoOffenders(AllowList.Where(e => !found.Contains(e)).Select(e => $"{e.Type}.{e.Method} is gone or internal, so remove its allow-list entry"), "A stale allow-list entry hides nothing:");
    }

    [Fact]
    public void TheChildrenAreTheEightOfTheRoadmap()
    {
        Assert.All(Children, t => Assert.True(t.IsPublic, t.Name));
        Assert.Equal(8, Children.Length);
    }
}

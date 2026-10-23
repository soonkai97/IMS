using System.Reflection;
using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Common;

public class PublicEntityTests
{
    /// <summary>The tables a client addresses (ADR docs/architecture/adr-guid-keys.md section 3): these, and only these, carry a public id.</summary>
    private static readonly Type[] Addressed =
    [
        typeof(User), typeof(Warehouse), typeof(Partner), typeof(Category), typeof(Uom), typeof(Product), typeof(ApprovalRule), typeof(StockDocument), typeof(Attachment), typeof(Role), typeof(PurchaseOrder), typeof(SalesOrder)
    ];

    private static PublicEntity Blank(Type type) => (PublicEntity)Activator.CreateInstance(type, nonPublic: true)!;

    [Fact]
    public void Exactly_the_eleven_addressed_entities_have_a_public_id()
    {
        var actual = typeof(PublicEntity).Assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(PublicEntity).IsAssignableFrom(t));

        Assert.Equal(Addressed.Select(t => t.Name).Order(), actual.Select(t => t.Name).Order());
    }

    [Fact]
    public void A_new_entity_gets_a_random_version_4_id()
    {
        foreach (var type in Addressed)
        {
            var id = Blank(type).PublicId;

            Assert.NotEqual(Guid.Empty, id);
            Assert.Equal('4', id.ToString()[14]); // the version digit of an RFC 4122 random GUID: no counter, no timestamp
        }
    }

    [Fact]
    public void Two_entities_never_share_an_id_and_the_ids_are_not_in_order()
    {
        var ids = Enumerable.Range(0, 2_000).Select(_ => Blank(typeof(Product)).PublicId).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        // A sequential generator would make (almost) every pair ascend; a random one about half of them.
        var ascending = ids.Zip(ids.Skip(1), (a, b) => a.CompareTo(b) < 0).Count(up => up);
        Assert.InRange(ascending, 700, 1_300);
    }

    [Fact]
    public void The_factories_make_the_id_and_nothing_can_set_it_from_outside()
    {
        var uom = Uom.Create("PCS");
        var warehouse = Warehouse.Create("WH1", "Main");

        Assert.NotEqual(Guid.Empty, uom.PublicId);
        Assert.NotEqual(uom.PublicId, warehouse.PublicId);

        var property = typeof(PublicEntity).GetProperty(nameof(PublicEntity.PublicId))!;
        Assert.True(property.SetMethod!.IsPrivate, "PublicId must have a private setter: only the constructor makes it and the database reads it back.");
        Assert.DoesNotContain(typeof(PublicEntity).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
            m => m.Name.Contains("PublicId", StringComparison.Ordinal) && m != property.SetMethod && m != property.GetMethod);
    }

    [Fact]
    public void Changing_an_entity_does_not_change_its_id()
    {
        var warehouse = NewWarehouse();
        var id = warehouse.PublicId;

        warehouse.Update("WH2", "Renamed", null, null, false);
        warehouse.WithId(99);

        Assert.Equal(id, warehouse.PublicId);
    }
}

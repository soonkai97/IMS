using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Identity;

/// <summary>
/// DDD-15: <see cref="Role"/> is an aggregate root that raises an event after each valid change, carrying the ids and the facts the audit row prints today (docs/architecture/ddd-roadmap.md 2.4) and nothing
/// else. A refused change raises nothing. The role's own state is unchanged by raising: <c>RoleTests</c> still pass as they were.
/// </summary>
public class RoleEventsTests
{
    private static Role Custom(params string[] permissions) => Role.Create("Buyers", "Buyers", null, false, permissions).WithId(9);

    [Fact]
    public void Create_RaisesOneRoleCreated_WithTheIdTheCodeTheNameAndThePermissionsInCatalogOrder()
    {
        var role = Role.Create("Buyers", "  Buyers  ", "text", false, [Permissions.StockRead, Permissions.MasterDataRead]);

        var created = Assert.IsType<RoleCreated>(Assert.Single(role.DomainEvents));
        Assert.Equal((role.PublicId, "Buyers", "Buyers"), (created.RoleId, created.Code, created.Name));   // the name as stored: trimmed
        Assert.Equal([Permissions.MasterDataRead, Permissions.StockRead], created.Permissions);   // the catalog order of the role editor, not the order given
        Assert.Null(created.CopiedFrom);
    }

    [Fact]
    public void Create_AsACopy_NamesTheCodeOfTheSource()
    {
        var role = Role.Create("Copy", "Copy", null, false, [Permissions.MasterDataRead], copiedFrom: "Buyers");

        Assert.Equal("Buyers", Assert.IsType<RoleCreated>(Assert.Single(role.DomainEvents)).CopiedFrom);
    }

    [Fact]
    public void Create_Refused_RaisesNothing()
    {
        Assert.Throws<DomainException>(() => Role.Create("Buyers", "", null, false, [Permissions.StockRead]));
        Assert.Throws<DomainException>(() => Role.Create("Buyers", "Buyers", null, false, ["not.a.permission"]));
    }

    [Fact]
    public void Update_RaisesRoleUpdated_WithTheStateBeforeAndAfter_EvenWhenNothingChanged()
    {
        var role = Custom(Permissions.MasterDataRead);
        role.ClearDomainEvents();

        role.Update("Renamed", "new text", active: false);
        role.Update("Renamed", "new text", active: false);

        var events = role.DomainEvents.Cast<RoleUpdated>().ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal((role.PublicId, "Buyers", new RoleSummary("Buyers", "", true), new RoleSummary("Renamed", "new text", false)), (events[0].RoleId, events[0].Code, events[0].Before, events[0].After));
        Assert.Equal((new RoleSummary("Renamed", "new text", false), new RoleSummary("Renamed", "new text", false)), (events[1].Before, events[1].After));   // the row is written whatever changed: as the handler always did
    }

    [Fact]
    public void Update_OfASystemRole_IsRefusedAndRaisesNothing()
    {
        var role = Role.Create(SystemRoleCodes.Owner, "Owner", null, true, SystemRoleDefaults.For(SystemRoleCodes.Owner)).WithId(3);
        role.ClearDomainEvents();

        Assert.Throws<LockedRoleException>(() => role.Update("Other name", null, active: true));
        Assert.Throws<LockedRoleException>(() => role.Update("Owner", null, active: false));

        Assert.Empty(role.DomainEvents);
    }

    [Fact]
    public void Update_WithATextThatIsNotAcceptable_IsRefusedAndRaisesNothing()
    {
        var role = Custom(Permissions.MasterDataRead);
        role.ClearDomainEvents();

        Assert.Throws<DomainException>(() => role.Update("", null, active: true));

        Assert.Empty(role.DomainEvents);
        Assert.Equal("Buyers", role.Name);
    }

    [Fact]
    public void SetPermissions_RaisesRolePermissionsChanged_WithWhatWasAddedAndRemovedInCatalogOrder()
    {
        var role = Custom(Permissions.MasterDataRead, Permissions.StockRead, Permissions.ReportsRead);
        role.ClearDomainEvents();

        role.SetPermissions([Permissions.MasterDataRead, Permissions.DocumentCreate, Permissions.MasterDataManage]);

        var changed = Assert.IsType<RolePermissionsChanged>(Assert.Single(role.DomainEvents));
        Assert.Equal((role.PublicId, "Buyers"), (changed.RoleId, changed.Code));
        Assert.Equal([Permissions.MasterDataManage, Permissions.DocumentCreate], changed.Added);
        Assert.Equal([Permissions.StockRead, Permissions.ReportsRead], changed.Removed);
    }

    [Fact]
    public void SetPermissions_WithTheSameSet_RaisesNothing()
    {
        var role = Custom(Permissions.MasterDataRead, Permissions.StockRead);
        role.ClearDomainEvents();

        role.SetPermissions([Permissions.StockRead, Permissions.MasterDataRead]);

        Assert.Empty(role.DomainEvents);
    }

    [Fact]
    public void SetPermissions_OfALockedRole_IsRefusedAndRaisesNothing()
    {
        var admin = Role.Create(SystemRoleCodes.Admin, "Administrator", null, true, PermissionCatalog.Codes).WithId(1);
        admin.ClearDomainEvents();

        Assert.Throws<LockedRoleException>(() => admin.SetPermissions([Permissions.StockRead]));

        Assert.Empty(admin.DomainEvents);
    }

    [Fact]
    public void Delete_RaisesRoleDeleted_WithTheCodeTheNameAndThePermissionsInCatalogOrder()
    {
        var role = Custom(Permissions.StockRead, Permissions.MasterDataRead);
        role.ClearDomainEvents();

        role.Delete();

        var deleted = Assert.IsType<RoleDeleted>(Assert.Single(role.DomainEvents));
        Assert.Equal((role.PublicId, "Buyers", "Buyers"), (deleted.RoleId, deleted.Code, deleted.Name));
        Assert.Equal([Permissions.MasterDataRead, Permissions.StockRead], deleted.Permissions);
        Assert.True(role.RecDeleted);
    }

    [Fact]
    public void Delete_OfASystemRole_IsRefusedAndRaisesNothing()
    {
        var role = Role.Create(SystemRoleCodes.Clerk, "Clerk", null, true, SystemRoleDefaults.For(SystemRoleCodes.Clerk)).WithId(5);
        role.ClearDomainEvents();

        Assert.Throws<LockedRoleException>(role.Delete);

        Assert.Empty(role.DomainEvents);
        Assert.False(role.RecDeleted);
    }

    [Fact]
    public void TheEventsCarryIdsAndFacts_NeverTheRole()
    {
        var role = Custom(Permissions.MasterDataRead);
        role.SetPermissions([Permissions.MasterDataRead, Permissions.StockRead]);
        role.Update("Renamed", null, true);
        role.Delete();

        Assert.All(role.DomainEvents, e => Assert.DoesNotContain(e.GetType().GetProperties(), p => typeof(Role).IsAssignableFrom(p.PropertyType) || typeof(RolePermission).IsAssignableFrom(p.PropertyType)));
        Assert.Equal(role.RaisedEvents.Count, role.DomainEvents.Count);
    }
}

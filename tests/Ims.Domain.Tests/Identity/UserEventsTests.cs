using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Identity;

/// <summary>
/// DDD-15: <see cref="User"/> is an aggregate root that raises an event after each valid change, carrying ids and facts and never a password, a hash or a token (the audit row of today names none of them, and an
/// event is what a later subscriber reads). A refused change raises nothing. <c>UserTests</c> still pass as they were.
/// </summary>
public class UserEventsTests
{
    private const string OldHash = "old-hash-value";
    private const string NewHash = "new-hash-value";

    private static User NewUser(params int[] warehouses) => User.Create("alice", "alice@example.com", "Alice", NewRole(Roles.Clerk), OldHash, warehouses, allWarehouses: false);

    [Fact]
    public void Create_RaisesNothing_AndMarkCreated_RaisesOneUserCreated_WithTheIdTheTrimmedNameTheRoleCodeAndTheScope()
    {
        var user = User.Create("  alice ", "alice@example.com", "Alice", NewRole(Roles.Supervisor), OldHash, allWarehouses: true);

        Assert.Empty(user.DomainEvents);   // the factory raises nothing: the use case announces the user once the row is written, so the rows of the audit keep the order they always had

        user.MarkCreated();

        var created = Assert.IsType<UserCreated>(Assert.Single(user.DomainEvents));
        Assert.Equal((user.PublicId, "alice", Roles.Supervisor, true), (created.UserId, created.Username, created.RoleCode, created.AllWarehouses));
    }

    [Fact]
    public void Update_RaisesUserUpdated_WithTheStateBeforeAndAfter_AndTheNewWarehousesInTheOrderGiven()
    {
        var user = NewUser(1, 2);
        var supervisor = NewRole(Roles.Supervisor, id: 3);

        user.Update("alice2", "alice2@example.com", "Alice Two", supervisor, isActive: false, allWarehouses: true, warehouseIds: [9, 4], passwordHash: null);

        var updated = Assert.IsType<UserUpdated>(Assert.Single(user.DomainEvents));
        Assert.Equal(user.PublicId, updated.UserId);
        Assert.Equal(("alice", Roles.Clerk, false, true), (updated.Before.Username, updated.Before.RoleCode, updated.Before.AllWarehouses, updated.Before.IsActive));
        Assert.Equal([1, 2], updated.Before.WarehouseIds);
        Assert.Equal(("alice2", Roles.Supervisor, true, false), (updated.After.Username, updated.After.RoleCode, updated.After.AllWarehouses, updated.After.IsActive));
        Assert.Equal([9, 4], updated.After.WarehouseIds);   // as given, not as the aggregate happens to hold them
        Assert.Equal((Roles.Supervisor, "alice2@example.com", "Alice Two", false, true), (user.Role.Code, user.Email, user.DisplayName, user.IsActive, user.AllWarehouses));
        Assert.Equal([4, 9], user.Warehouses.Select(w => w.WarehouseId).Order());
    }

    [Fact]
    public void Update_WithNothingChanged_StillRaisesTheEvent_AsTheHandlerAlwaysWroteTheRow()
    {
        var user = NewUser(1);

        user.Update("alice", "alice@example.com", "Alice", user.Role, isActive: true, allWarehouses: false, warehouseIds: [1], passwordHash: null);

        var updated = Assert.IsType<UserUpdated>(Assert.Single(user.DomainEvents));
        Assert.Equal(updated.Before, updated.After, new SummaryComparer());
    }

    [Fact]
    public void Update_WithAPasswordHash_StoresItAndTheEventNeverCarriesIt()
    {
        var user = NewUser(1);

        user.Update("alice", "alice@example.com", "Alice", user.Role, isActive: true, allWarehouses: false, warehouseIds: [1], passwordHash: NewHash);

        Assert.Equal(NewHash, user.PasswordHash);
        var text = string.Join("|", user.DomainEvents.Select(e => e.ToString()));
        Assert.DoesNotContain(NewHash, text);
        Assert.DoesNotContain(OldHash, text);
        Assert.DoesNotContain("hash", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Update_WithoutAPasswordHash_KeepsTheHash()
    {
        var user = NewUser(1);

        user.Update("alice", "alice@example.com", "Alice", user.Role, isActive: true, allWarehouses: false, warehouseIds: [1], passwordHash: null);

        Assert.Equal(OldHash, user.PasswordHash);
    }

    [Theory]
    [InlineData("", "alice@example.com", true)]
    [InlineData("alice", " ", true)]
    [InlineData("alice", "alice@example.com", false)]   // a role that is not active
    public void Update_Refused_RaisesNothingAndChangesNothing(string username, string email, bool roleActive)
    {
        var user = NewUser(1);
        var target = NewRole(Roles.Supervisor, id: 3);
        if (!roleActive)
        {
            target = Role.Create("Gone", "Gone", null, false, []).WithId(4);   // a system role cannot be deactivated, a custom one can
            target.SetActive(false);
        }

        Assert.Throws<DomainException>(() => user.Update(username, email, "Alice", target, isActive: true, allWarehouses: true, warehouseIds: [7], passwordHash: NewHash));

        Assert.Empty(user.DomainEvents);
        Assert.Equal(("alice", OldHash, Roles.Clerk, false), (user.Username, user.PasswordHash, user.Role.Code, user.AllWarehouses));
        Assert.Equal([1], user.Warehouses.Select(w => w.WarehouseId));
    }

    private sealed class SummaryComparer : IEqualityComparer<UserSummary>
    {
        public bool Equals(UserSummary? x, UserSummary? y) =>
            x is not null && y is not null && (x.Username, x.RoleCode, x.AllWarehouses, x.IsActive) == (y.Username, y.RoleCode, y.AllWarehouses, y.IsActive) && x.WarehouseIds.SequenceEqual(y.WarehouseIds);

        public int GetHashCode(UserSummary obj) => obj.Username.GetHashCode(StringComparison.Ordinal);
    }
}

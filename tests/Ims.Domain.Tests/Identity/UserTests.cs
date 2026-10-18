using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Identity;

public class UserTests
{
    private static User NewUser(string role = Roles.Clerk) => User.Create("alice", "alice@example.com", "Alice", NewRole(role), "hash");

    [Theory]
    [InlineData(Roles.Admin, true, true)]
    [InlineData(Roles.Admin, false, false)]      // a deactivated person administers nothing
    [InlineData(Roles.Owner, true, false)]       // a live role without the whole core
    public void IsAdministrator_IsTheRolesCoreAndTheUsersActivity_NotTheRoleName(string role, bool active, bool expected)
    {
        Assert.Equal(expected, User.Create("alice", "alice@example.com", "Alice", NewRole(role), "hash", isActive: active).IsAdministrator);
    }

    // ----- Create / profile -----
    [Fact]
    public void Create_TrimsFieldsAndKeepsHashAndRole()
    {
        var user = User.Create("  alice ", " alice@example.com ", " Alice A ", NewRole(Roles.Supervisor), "hash", [3, 3, 4]);

        Assert.Equal("alice", user.Username);
        Assert.Equal("alice@example.com", user.Email);
        Assert.Equal("Alice A", user.DisplayName);
        Assert.Equal(Roles.Supervisor, user.Role.Code);
        Assert.Equal("hash", user.PasswordHash);
        Assert.True(user.IsActive);
        Assert.Equal([3, 4], user.Warehouses.Select(w => w.WarehouseId).Order());
    }

    [Fact]
    public void Create_TakesTheRoleRow_AndSeesNoWarehouseUnlessToldTo()
    {
        var role = NewRole(Roles.Admin, id: 41); // not even the Admin role decides the scope: it is a choice per person

        var user = User.Create("alice", "alice@example.com", "Alice", role, "hash");

        Assert.Same(role, user.Role);
        Assert.Equal(41, user.RoleId);
        Assert.False(user.AllWarehouses);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_TakesTheScopeFlagAsGiven(bool all)
    {
        var user = User.Create("alice", "alice@example.com", "Alice", NewRole(Roles.Clerk), "hash", allWarehouses: all);

        Assert.Equal(all, user.AllWarehouses);
    }

    [Fact]
    public void UpdateProfile_MovesTheRoleAndLeavesTheScopeFlagAlone_SetAllWarehousesChangesIt()
    {
        var user = User.Create("alice", "alice@example.com", "Alice", NewRole(Roles.Admin, id: 1), "hash", allWarehouses: true);

        user.UpdateProfile("alice", "alice@example.com", "Alice", NewRole(Roles.Clerk, id: 5), true);

        Assert.Equal((Roles.Clerk, 5, true), (user.Role.Code, user.RoleId, user.AllWarehouses));

        user.SetAllWarehouses(false);

        Assert.False(user.AllWarehouses);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_BlankUsername_Throws(string username)
    {
        var ex = Assert.Throws<DomainException>(() => User.Create(username, "a@b.c", "A", NewRole(Roles.Clerk), "h"));

        Assert.Equal(Messages.UsernameInvalid, ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_BlankEmail_Throws(string email)
    {
        var ex = Assert.Throws<DomainException>(() => User.Create("alice", email, "A", NewRole(Roles.Clerk), "h"));

        Assert.Equal(Messages.EmailInvalid, ex.Message);
    }

    [Fact]
    public void Create_ACustomRoleIsAsGoodAsASystemRole()
    {
        var user = User.Create("alice", "a@b.c", "A", NewRole("Janitor", id: 9), "h");

        Assert.Equal(("Janitor", 9), (user.Role.Code, user.RoleId));
    }

    [Fact]
    public void Create_AnInactiveRole_Throws()
    {
        var inactive = NewRole(Roles.Clerk).WithPrivate("IsActive", false);

        var ex = Assert.Throws<DomainException>(() => User.Create("alice", "a@b.c", "A", inactive, "h"));

        Assert.Equal(Messages.RoleInactive, ex.Message);
    }

    [Fact]
    public void Create_ARoleThatIsNotASavedRow_Throws()
    {
        var unsaved = Role.Create(Roles.Clerk, "Clerk", null, true, []);

        var ex = Assert.Throws<DomainException>(() => User.Create("alice", "a@b.c", "A", unsaved, "h"));

        Assert.Equal(Messages.UnknownRole, ex.Message);
    }

    [Fact]
    public void Create_NullDisplayName_BecomesEmpty()
    {
        var user = User.Create("alice", "a@b.c", null!, NewRole(Roles.Clerk), "h");

        Assert.Equal("", user.DisplayName);
    }

    [Fact]
    public void UpdateProfile_Invalid_LeavesProfileUntouched()
    {
        var user = NewUser();

        Assert.Throws<DomainException>(() => user.UpdateProfile("bob", "b@b.c", "Bob", NewRole("Janitor").WithPrivate("IsActive", false), false));

        Assert.Equal("alice", user.Username);
        Assert.Equal(Roles.Clerk, user.Role.Code);
        Assert.True(user.IsActive);
    }

    [Fact]
    public void ChangePasswordHash_ReplacesTheHash()
    {
        var user = NewUser();

        user.ChangePasswordHash("new-hash");

        Assert.Equal("new-hash", user.PasswordHash);
    }

    // ----- Warehouse scope -----
    [Fact]
    public void AssignWarehouses_MakesTheSetExact_AddingAndRemoving()
    {
        var user = User.Create("a", "a@b.c", "A", NewRole(Roles.Clerk), "h", [1, 2]);
        var kept = user.Warehouses.Single(w => w.WarehouseId == 2);

        user.AssignWarehouses([2, 3, 3]);

        Assert.Equal([2, 3], user.Warehouses.Select(w => w.WarehouseId).Order());
        Assert.Same(kept, user.Warehouses.Single(w => w.WarehouseId == 2));
    }

    [Fact]
    public void AssignWarehouses_Empty_RemovesAll()
    {
        var user = User.Create("a", "a@b.c", "A", NewRole(Roles.Clerk), "h", [1, 2]);

        user.AssignWarehouses([]);

        Assert.Empty(user.Warehouses);
    }

    // ----- Lockout (AUTH-01): 5 failures -> 15 minutes -----
    [Fact]
    public void NewUser_IsNotLockedOut()
    {
        Assert.False(NewUser().IsLockedOut(Now));
    }

    [Fact]
    public void RegisterFailedLogin_FourFailures_DoNotLock()
    {
        var user = NewUser();

        for (var i = 0; i < Limits.MaxFailedLogins - 1; i++)
            user.RegisterFailedLogin(Now);

        Assert.False(user.IsLockedOut(Now));
        Assert.Equal(Limits.MaxFailedLogins - 1, user.FailedLoginCount);
        Assert.Null(user.LockoutUntil);
    }

    [Fact]
    public void RegisterFailedLogin_FifthFailure_LocksForFifteenMinutesAndResetsCounter()
    {
        var user = NewUser();

        for (var i = 0; i < 5; i++)
            user.RegisterFailedLogin(Now);

        Assert.Equal(Now.AddMinutes(15), user.LockoutUntil);
        Assert.Equal(TimeSpan.FromMinutes(15), User.LockoutDuration);
        Assert.True(user.IsLockedOut(Now));
        Assert.Equal(0, user.FailedLoginCount);
    }

    [Fact]
    public void IsLockedOut_BecomesFalseExactlyWhenTheLockoutEnds()
    {
        var user = NewUser();
        for (var i = 0; i < 5; i++)
            user.RegisterFailedLogin(Now);

        Assert.True(user.IsLockedOut(Now.AddMinutes(15).AddTicks(-1)));
        Assert.False(user.IsLockedOut(Now.AddMinutes(15)));
        Assert.False(user.IsLockedOut(Now.AddMinutes(16)));
    }

    [Fact]
    public void RegisterFailedLogin_AfterLockoutExpired_NeedsFiveMoreFailuresToLockAgain()
    {
        var user = NewUser();
        for (var i = 0; i < 5; i++)
            user.RegisterFailedLogin(Now);
        var later = Now.AddMinutes(20);

        for (var i = 0; i < 4; i++)
            user.RegisterFailedLogin(later);

        Assert.False(user.IsLockedOut(later));
        user.RegisterFailedLogin(later);
        Assert.True(user.IsLockedOut(later));
        Assert.Equal(later.AddMinutes(15), user.LockoutUntil);
    }

    [Fact]
    public void RegisterSuccessfulLogin_ResetsFailuresAndClearsLockout()
    {
        var user = NewUser();
        for (var i = 0; i < 3; i++)
            user.RegisterFailedLogin(Now);

        user.RegisterSuccessfulLogin();

        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockoutUntil);
    }

    [Fact]
    public void RegisterSuccessfulLogin_AfterLockout_UnlocksTheAccount()
    {
        var user = NewUser();
        for (var i = 0; i < 5; i++)
            user.RegisterFailedLogin(Now);

        user.RegisterSuccessfulLogin();

        Assert.False(user.IsLockedOut(Now));
    }

    [Fact]
    public void FailuresDoNotAccumulateAcrossASuccess()
    {
        var user = NewUser();
        for (var i = 0; i < 4; i++)
            user.RegisterFailedLogin(Now);
        user.RegisterSuccessfulLogin();

        for (var i = 0; i < 4; i++)
            user.RegisterFailedLogin(Now);

        Assert.False(user.IsLockedOut(Now));
    }
}

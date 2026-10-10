using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Identity;

public class UserTests
{
    private static User NewUser(string role = Roles.Clerk) => User.Create("alice", "alice@example.com", "Alice", role, "hash");

    // ----- Create / profile -----
    [Fact]
    public void Create_TrimsFieldsAndKeepsHashAndRole()
    {
        var user = User.Create("  alice ", " alice@example.com ", " Alice A ", Roles.Supervisor, "hash", [3, 3, 4]);

        Assert.Equal("alice", user.Username);
        Assert.Equal("alice@example.com", user.Email);
        Assert.Equal("Alice A", user.DisplayName);
        Assert.Equal(Roles.Supervisor, user.Role);
        Assert.Equal("hash", user.PasswordHash);
        Assert.True(user.IsActive);
        Assert.Equal([3, 4], user.Warehouses.Select(w => w.WarehouseId).Order());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_BlankUsername_Throws(string username)
    {
        var ex = Assert.Throws<DomainException>(() => User.Create(username, "a@b.c", "A", Roles.Clerk, "h"));

        Assert.Equal(Messages.UsernameInvalid, ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_BlankEmail_Throws(string email)
    {
        var ex = Assert.Throws<DomainException>(() => User.Create("alice", email, "A", Roles.Clerk, "h"));

        Assert.Equal(Messages.EmailInvalid, ex.Message);
    }

    [Fact]
    public void Create_UnknownRole_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => User.Create("alice", "a@b.c", "A", "Janitor", "h"));

        Assert.Equal(Messages.UnknownRole, ex.Message);
    }

    [Fact]
    public void Create_NullDisplayName_BecomesEmpty()
    {
        var user = User.Create("alice", "a@b.c", null!, Roles.Clerk, "h");

        Assert.Equal("", user.DisplayName);
    }

    [Fact]
    public void UpdateProfile_Invalid_LeavesProfileUntouched()
    {
        var user = NewUser();

        Assert.Throws<DomainException>(() => user.UpdateProfile("bob", "b@b.c", "Bob", "Janitor", false));

        Assert.Equal("alice", user.Username);
        Assert.Equal(Roles.Clerk, user.Role);
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
        var user = User.Create("a", "a@b.c", "A", Roles.Clerk, "h", [1, 2]);
        var kept = user.Warehouses.Single(w => w.WarehouseId == 2);

        user.AssignWarehouses([2, 3, 3]);

        Assert.Equal([2, 3], user.Warehouses.Select(w => w.WarehouseId).Order());
        Assert.Same(kept, user.Warehouses.Single(w => w.WarehouseId == 2));
    }

    [Fact]
    public void AssignWarehouses_Empty_RemovesAll()
    {
        var user = User.Create("a", "a@b.c", "A", Roles.Clerk, "h", [1, 2]);

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

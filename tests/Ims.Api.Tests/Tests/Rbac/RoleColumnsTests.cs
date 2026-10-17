using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Settings;
using Ims.Application.Features.Users;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Rbac;

/// <summary>
/// A role is data (RP-4b): a user has a role row (<c>RoleId</c>) and a scope flag (<c>AllWarehouses</c>) and nothing else, an approval rule has a tier and nothing else. Every
/// way a user or a rule gets written (the seed, the API) is checked against those columns, and the role of a user is whatever the Role table says, system role or not.
/// Real SQL Server, through the running host.
/// </summary>
[Collection(ApiCollection.Name)]
public class RoleColumnsTests(ApiFixture fixture)
{
    private readonly AccountFactory _accounts = new(fixture);

    private async Task<(string RoleCode, bool AllWarehouses)> UserRowAsync(string username)
    {
        var code = await fixture.Database.ScalarAsync<string>("SELECT r.Code FROM [User] u JOIN Role r ON r.Id = u.RoleId WHERE u.Username = @u", ("@u", username));
        var all = await fixture.Database.ScalarAsync<bool>("SELECT AllWarehouses FROM [User] WHERE Username = @u", ("@u", username));
        return (code, all);
    }

    /// <summary>A role nobody invented in the code: the Role table is the only place that knows it.</summary>
    private async Task<Role> CustomRoleAsync(bool active = true, params string[] permissions)
    {
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = Role.Create(Unique.Code("R"), Unique.Name("Role"), null, false, permissions);
        db.Add(role);
        await db.SaveChangesAsync();
        if (!active) await fixture.Database.ExecuteAsync("UPDATE [Role] SET IsActive = 0 WHERE Id = @i", ("@i", role.Id));
        return role;
    }

    private UserSaveRequest NewUser(string role, bool? allWarehouses = null, bool isActive = true)
    {
        var username = Unique.Username("rc");
        return new UserSaveRequest(username, Unique.Email(username), username, role, isActive, $"Pw-{Guid.NewGuid():N}-1!", [fixture.Reference.DefaultWarehouseId], allWarehouses);
    }

    [Fact]
    public async Task The_legacy_role_columns_are_gone_from_the_database()
    {
        Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'[User]') AND name = N'Role'"));
        Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'[ApprovalRule]') AND name = N'RoleRequired'"));
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public async Task The_seeded_user_of_each_role_points_at_its_role_row_and_sees_every_warehouse_as_the_seed_says(string role)
    {
        var row = await UserRowAsync(TestUsers.UsernameOf(role));

        Assert.Equal((role, TestScope.SeesAllWarehouses(role)), row);
    }

    [Theory]
    [InlineData(Roles.Admin, true, true)]
    [InlineData(Roles.Owner, false, false)]
    [InlineData(Roles.Clerk, true, true)]
    [InlineData(Roles.Admin, null, false)] // not told: a new user sees no warehouse, whatever the role
    public async Task A_user_created_through_the_api_keeps_the_scope_flag_it_was_given_whatever_its_role(string role, bool? all, bool expected)
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var created = (await admin.Users.CreateAsync(NewUser(role, all))).Data<UserDto>();

        Assert.Equal((role, expected), (created.Role, created.AllWarehouses));
        Assert.Equal((role, expected), await UserRowAsync(created.Username));
    }

    [Fact]
    public async Task Changing_the_role_of_a_user_moves_the_role_row_and_leaves_the_scope_flag_to_the_request()
    {
        var account = await _accounts.CreateAsync(Roles.Admin); // sees every warehouse
        var admin = await fixture.AsAsync(Roles.Admin);
        var firstRoleId = await fixture.Database.ScalarAsync<int>("SELECT RoleId FROM [User] WHERE Username = @u", ("@u", account.Username));
        UserSaveRequest Save(string role, bool? all) => new(account.Username, account.Email, account.Username, role, true, null, account.WarehouseIds.ToList(), all);

        // The flag is not left to the role: not said, it stays; said, it moves.
        (await admin.Users.UpdateAsync(account.Id, Save(Roles.Clerk, null))).ShouldBeOk();
        Assert.Equal((Roles.Clerk, true), await UserRowAsync(account.Username));

        (await admin.Users.UpdateAsync(account.Id, Save(Roles.Owner, false))).ShouldBeOk();
        Assert.Equal((Roles.Owner, false), await UserRowAsync(account.Username));

        (await admin.Users.UpdateAsync(account.Id, Save(Roles.Supervisor, true))).ShouldBeOk();
        Assert.Equal((Roles.Supervisor, true), await UserRowAsync(account.Username));
        Assert.NotEqual(firstRoleId, await fixture.Database.ScalarAsync<int>("SELECT RoleId FROM [User] WHERE Username = @u", ("@u", account.Username)));
    }

    [Fact]
    public async Task A_user_can_be_given_a_role_that_exists_only_in_the_role_table_and_gets_its_permissions()
    {
        var custom = await CustomRoleAsync(true, Permissions.StockRead, Permissions.ReportsRead);
        var admin = await fixture.AsAsync(Roles.Admin);
        var request = NewUser(custom.Code);

        var created = (await admin.Users.CreateAsync(request)).Data<UserDto>();
        var auth = (await fixture.CreateClient().Auth.LoginAsync(request.Username, request.Password!)).Data<AuthResult>();

        Assert.Equal(custom.Code, created.Role);
        Assert.Equal((custom.Code, false), await UserRowAsync(request.Username));
        Assert.Equal([Permissions.ReportsRead, Permissions.StockRead], auth.User.Permissions.Order(StringComparer.Ordinal));
        Assert.Equal(custom.Code, auth.User.Role);
        var listed = (await admin.GetAsync($"/api/users?role={custom.Code}")).Data<Ims.Application.PagedResult<UserDto>>();
        Assert.Equal(request.Username, Assert.Single(listed.Items).Username);
    }

    [Fact]
    public async Task A_role_that_is_not_in_the_role_table_or_not_active_is_refused_with_a_400_on_create_and_update()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var inactive = await CustomRoleAsync(active: false, Permissions.StockRead);
        var account = await _accounts.CreateAsync(Roles.Clerk);
        UserSaveRequest Update(string role) => new(account.Username, account.Email, account.Username, role, true, null, account.WarehouseIds.ToList());

        foreach (var role in new[] { "Janitor", inactive.Code })
        {
            (await admin.Users.CreateAsync(NewUser(role))).ShouldBeProblem(HttpStatusCode.BadRequest);
            (await admin.Users.UpdateAsync(account.Id, Update(role))).ShouldBeProblem(HttpStatusCode.BadRequest);
        }
        (await admin.Users.CreateAsync(NewUser(""))).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal((Roles.Clerk, false), await UserRowAsync(account.Username)); // nothing moved
    }

    [Fact]
    public async Task Only_a_user_who_sees_every_warehouse_can_let_someone_see_every_warehouse()
    {
        // A delegated user manager: a role that holds users.manage and is scoped to one warehouse.
        var manager = await CustomRoleAsync(true, Permissions.UsersManage, Permissions.UsersRead);
        var admin = await fixture.AsAsync(Roles.Admin);
        var request = NewUser(manager.Code, allWarehouses: false);
        (await admin.Users.CreateAsync(request)).ShouldBeOk();
        var client = fixture.CreateClient();
        client.AccessToken = (await client.Auth.LoginAsync(request.Username, request.Password!)).Data<AuthResult>().AccessToken;

        (await client.Users.CreateAsync(NewUser(manager.Code, allWarehouses: true))).ShouldBeProblem(HttpStatusCode.Forbidden);
        var granted = (await client.Users.CreateAsync(NewUser(manager.Code, allWarehouses: false))).Data<UserDto>();
        (await client.Users.UpdateAsync(granted.Id, new UserSaveRequest(granted.Username, granted.Email, granted.Username, manager.Code, true, null, [fixture.Reference.DefaultWarehouseId], true)))
            .ShouldBeProblem(HttpStatusCode.Forbidden);
        Assert.False((await UserRowAsync(granted.Username)).AllWarehouses);
    }

    // ----- Approval rules: the tier is the only record; roleRequired is a deprecated way to say and to read it -----
    private Task<T> RuleColumnAsync<T>(Guid publicId, string column) =>
        fixture.Database.ScalarAsync<T>($"SELECT [{column}] FROM ApprovalRule WHERE PublicId = @p", ("@p", publicId));

    [Theory]
    [InlineData(Roles.Admin, ApproverTier.Large)]
    [InlineData(Roles.Owner, ApproverTier.Large)]
    [InlineData(Roles.Supervisor, ApproverTier.Standard)]
    [InlineData(Roles.WarehouseStaff, ApproverTier.Standard)]
    [InlineData(Roles.Clerk, ApproverTier.Standard)]
    public async Task A_rule_saved_with_the_deprecated_role_name_stores_its_tier_and_shows_the_role_that_holds_the_tier(string role, ApproverTier tier)
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var other = tier == ApproverTier.Standard ? Roles.Owner : Roles.Supervisor;
        var shown = tier == ApproverTier.Large ? Roles.Owner : Roles.Supervisor;

        // Inactive from the start, so the rule never changes what another test's documents need.
        var created = (await admin.Settings.CreateRuleAsync(new ApprovalRuleSaveRequest(DocumentType.Receipt, 123_456m, null, role, IsActive: false))).Data<ApprovalRuleDto>();
        Assert.Equal((tier, shown), (created.ApproverTier, created.RoleRequired));
        Assert.Equal((int)tier, await RuleColumnAsync<int>(created.Id, "Tier"));

        var flipped = (await admin.Settings.UpdateRuleAsync(created.Id, new ApprovalRuleSaveRequest(DocumentType.Receipt, 123_456m, null, other, IsActive: false))).Data<ApprovalRuleDto>();
        Assert.NotEqual(tier, flipped.ApproverTier);
        Assert.Equal((int)flipped.ApproverTier, await RuleColumnAsync<int>(created.Id, "Tier"));

        var listed = (await admin.Settings.ListRulesAsync()).Data<List<ApprovalRuleDto>>().Single(r => r.Id == created.Id);
        Assert.Equal((flipped.ApproverTier, flipped.RoleRequired), (listed.ApproverTier, listed.RoleRequired));
    }

    [Theory]
    [InlineData(ApproverTier.Standard)]
    [InlineData(ApproverTier.Large)]
    public async Task A_rule_saved_with_a_tier_and_no_role_stores_the_tier(ApproverTier tier)
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var created = (await admin.Settings.CreateRuleAsync(new ApprovalRuleSaveRequest(DocumentType.Receipt, 123_457m, null, null, IsActive: false, ApproverTier: tier))).Data<ApprovalRuleDto>();

        Assert.Equal(tier, created.ApproverTier);
        Assert.Equal((int)tier, await RuleColumnAsync<int>(created.Id, "Tier"));
    }

    [Fact]
    public async Task A_rule_must_say_its_tier_once_and_without_contradiction()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        ApprovalRuleSaveRequest Rule(string? role, ApproverTier? tier) => new(DocumentType.Receipt, 123_458m, null, role, IsActive: false, ApproverTier: tier);

        (await admin.Settings.CreateRuleAsync(Rule(null, null))).ShouldBeProblem(HttpStatusCode.BadRequest);                       // says nothing
        (await admin.Settings.CreateRuleAsync(Rule("Janitor", null))).ShouldBeProblem(HttpStatusCode.BadRequest);                 // a role that is no system role
        (await admin.Settings.CreateRuleAsync(Rule(null, (ApproverTier)99))).ShouldBeProblem(HttpStatusCode.BadRequest);          // a tier that does not exist
        (await admin.Settings.CreateRuleAsync(Rule(Roles.Owner, ApproverTier.Standard))).ShouldBeProblem(HttpStatusCode.BadRequest); // two answers that disagree
        (await admin.Settings.CreateRuleAsync(Rule(Roles.Owner, ApproverTier.Large))).ShouldBeOk();                               // two answers that agree
    }
}

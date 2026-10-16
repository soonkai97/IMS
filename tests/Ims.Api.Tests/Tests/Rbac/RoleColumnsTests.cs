using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Settings;
using Ims.Application.Features.Users;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Rbac;

/// <summary>
/// RP-2 changes what the database stores, not what anyone may do: every way a user or an approval rule gets written (the seed, the API) must leave the new columns agreeing with
/// the old ones, because the API still decides by the role string and RP-4a/RP-4b switch to the new columns later. Real SQL Server, through the running host.
/// </summary>
[Collection(ApiCollection.Name)]
public class RoleColumnsTests(ApiFixture fixture)
{
    private readonly AccountFactory _accounts = new(fixture);

    private async Task<(string Role, int RoleId, string RoleCode, bool AllWarehouses)> UserRowAsync(string username)
    {
        var role = await fixture.Database.ScalarAsync<string>("SELECT Role FROM [User] WHERE Username = @u", ("@u", username));
        var roleId = await fixture.Database.ScalarAsync<int>("SELECT RoleId FROM [User] WHERE Username = @u", ("@u", username));
        var code = await fixture.Database.ScalarAsync<string>("SELECT r.Code FROM [User] u JOIN Role r ON r.Id = u.RoleId WHERE u.Username = @u", ("@u", username));
        var all = await fixture.Database.ScalarAsync<bool>("SELECT AllWarehouses FROM [User] WHERE Username = @u", ("@u", username));
        return (role, roleId, code, all);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public async Task The_seeded_user_of_each_role_points_at_its_role_row_and_sees_every_warehouse_exactly_when_its_role_did(string role)
    {
        var row = await UserRowAsync(TestUsers.UsernameOf(role));

        Assert.Equal(role, row.Role);
        Assert.Equal(role, row.RoleCode);
        Assert.Equal(Roles.SeesAllWarehouses(role), row.AllWarehouses);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public async Task A_user_created_through_the_api_gets_the_role_row_and_the_scope_flag_of_its_role(string role)
    {
        var account = await _accounts.CreateAsync(role);

        var row = await UserRowAsync(account.Username);

        Assert.Equal((role, role, Roles.SeesAllWarehouses(role)), (row.Role, row.RoleCode, row.AllWarehouses));
    }

    [Fact]
    public async Task Changing_the_role_of_a_user_moves_the_role_string_the_role_row_and_the_scope_flag_together()
    {
        var account = await _accounts.CreateAsync(Roles.Supervisor);
        var admin = await fixture.AsAsync(Roles.Admin);
        var firstRoleId = (await UserRowAsync(account.Username)).RoleId;

        foreach (var (role, seesAll) in new[] { (Roles.Owner, true), (Roles.Clerk, false), (Roles.Admin, true), (Roles.WarehouseStaff, false) })
        {
            var scope = seesAll ? [] : account.WarehouseIds.ToList();
            var request = new UserSaveRequest(account.Username, account.Email, account.Username, role, true, null, scope);
            (await admin.Users.UpdateAsync(account.Id, request)).ShouldBeOk();

            var row = await UserRowAsync(account.Username);
            Assert.Equal((role, role, seesAll), (row.Role, row.RoleCode, row.AllWarehouses));
        }
        Assert.NotEqual(firstRoleId, (await UserRowAsync(account.Username)).RoleId);
    }

    [Theory]
    [InlineData(Roles.Admin, ApproverTier.Large)]
    [InlineData(Roles.Owner, ApproverTier.Large)]
    [InlineData(Roles.Supervisor, ApproverTier.Standard)]
    [InlineData(Roles.WarehouseStaff, ApproverTier.Standard)]
    [InlineData(Roles.Clerk, ApproverTier.Standard)]
    public async Task Saving_an_approval_rule_writes_the_tier_with_the_role_on_create_and_on_every_update(string role, ApproverTier tier)
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var other = role == Roles.Supervisor ? Roles.Owner : Roles.Supervisor;

        // Inactive from the start, so the rule never changes what another test's documents need.
        var created = (await admin.Settings.CreateRuleAsync(new ApprovalRuleSaveRequest(DocumentType.Receipt, 123_456m, null, role, IsActive: false))).Data<ApprovalRuleDto>();
        Assert.Equal(role, await RuleColumnAsync<string>(created.Id, "RoleRequired"));
        Assert.Equal((int)tier, await RuleColumnAsync<int>(created.Id, "Tier"));

        (await admin.Settings.UpdateRuleAsync(created.Id, new ApprovalRuleSaveRequest(DocumentType.Receipt, 123_456m, null, other, IsActive: false))).ShouldBeOk();
        Assert.Equal(other == Roles.Owner ? (int)ApproverTier.Large : (int)ApproverTier.Standard, await RuleColumnAsync<int>(created.Id, "Tier"));

        (await admin.Settings.UpdateRuleAsync(created.Id, new ApprovalRuleSaveRequest(DocumentType.Receipt, 123_456m, null, role, IsActive: false))).ShouldBeOk();
        Assert.Equal((int)tier, await RuleColumnAsync<int>(created.Id, "Tier"));
    }

    private Task<T> RuleColumnAsync<T>(Guid publicId, string column) =>
        fixture.Database.ScalarAsync<T>($"SELECT [{column}] FROM ApprovalRule WHERE PublicId = @p", ("@p", publicId));
}

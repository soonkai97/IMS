using System.Net;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Roles;
using Ims.Application.Features.Users;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Rbac;

/// <summary>The roles API as the administrator uses it, the checks on what a role may hold, and what blocks a delete. Real SQL Server.</summary>
[Collection(ApiCollection.Name)]
public class RolesApiTests(ApiFixture fixture)
{
    private static readonly string[] AdminCore = [.. PermissionCatalog.All.Where(p => p.AdminCore).Select(p => p.Code)];

    private async Task<RoleDto> NewRoleAsync(ApiClient admin, params string[] permissions) => (await admin.CreateAsync(Unique.Name("Role"), permissions)).Data<RoleDto>();

    private async Task<List<AuditLog>> AuditOfAsync(string action, Guid roleId)
    {
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<AuditLog>().Where(a => a.Action == action && a.EntityType == nameof(Role) && a.EntityId == roleId.ToString()).ToListAsync();
    }

    private UserSaveRequest NewUser(string roleCode, bool active = true)
    {
        var username = Unique.Username("ru");
        return new UserSaveRequest(username, Unique.Email(username), "Role user", roleCode, active, $"Pw-{Guid.NewGuid():N}-1!", [fixture.Reference.DefaultWarehouseId]);
    }

    private static UserSaveRequest Move(UserDto user, string roleCode, bool? active = null) =>
        new(user.Username, user.Email, user.DisplayName, roleCode, active ?? user.IsActive, null, user.WarehouseIds);

    [Fact]
    public async Task The_administrator_creates_edits_replaces_the_permissions_of_and_deletes_a_custom_role_and_every_step_is_audited()
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var created = await admin.CreateAsync(Unique.Name("Night clerk"), Permissions.StockRead, Permissions.DocumentCreate);
        created.ShouldBeOk();
        var role = created.Read<RoleDto>();
        Assert.Matches("^NightClerk[A-Za-z0-9]*$", role.Code);                                // made from the name, never chosen by the client
        Assert.False(role.IsSystem);
        Assert.Equal([Permissions.DocumentCreate, Permissions.StockRead], role.Permissions);  // catalog order
        Assert.Equal($"\"{role.ConcurrencyToken}\"", created.Header("ETag"));
        Assert.Equal(role.Id, (await admin.GetAsync($"/api/roles/{role.Id}")).Data<RoleDto>().Id);
        Assert.Equal(0, role.UserCount);
        Assert.Single(await AuditOfAsync(AuditActions.RoleCreated, role.Id));

        var replaced = (await admin.ReplaceAsync(role, [Permissions.StockRead, Permissions.ReportsRead])).Data<RolePermissionsResult>();
        Assert.Equal([Permissions.ReportsRead], replaced.Added);
        Assert.Equal([Permissions.DocumentCreate], replaced.Removed);
        Assert.Equal([Permissions.StockRead, Permissions.ReportsRead], replaced.Role.Permissions);
        Assert.NotEqual(role.ConcurrencyToken, replaced.Role.ConcurrencyToken);               // the token covers the grants, not only the role row
        Assert.Single(await AuditOfAsync(AuditActions.RolePermissionsChanged, role.Id));

        var same = (await admin.ReplaceAsync(replaced.Role, [Permissions.ReportsRead, Permissions.StockRead])).Data<RolePermissionsResult>(); // the same set again
        Assert.Empty(same.Added);
        Assert.Empty(same.Removed);
        Assert.Equal(replaced.Role.ConcurrencyToken, same.Role.ConcurrencyToken);
        Assert.Single(await AuditOfAsync(AuditActions.RolePermissionsChanged, role.Id));

        var renamed = (await admin.UpdateRoleAsync(same.Role, name: Unique.Name("Evening clerk"), active: false)).Data<RoleDto>();
        Assert.False(renamed.IsActive);
        Assert.Equal(role.Code, renamed.Code);                                                // the code never changes
        Assert.Single(await AuditOfAsync(AuditActions.RoleUpdated, role.Id));
        Assert.DoesNotContain((await admin.GetAsync("/api/roles")).Data<List<RoleDto>>(), r => r.Id == role.Id); // inactive roles are not listed by default
        Assert.Contains((await admin.GetAsync("/api/roles?includeInactive=true")).Data<List<RoleDto>>(), r => r.Id == role.Id);

        (await admin.DeleteRoleAsync(renamed)).ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/roles/{role.Id}")).ShouldBeProblem(HttpStatusCode.NotFound);
        Assert.Single(await AuditOfAsync(AuditActions.RoleDeleted, role.Id));
        // The grants stay as rows, hidden with the role; nothing reads a grant without its role.
        Assert.Equal(2, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM RolePermission rp JOIN Role r ON r.Id = rp.RoleId WHERE r.PublicId = @id AND rp.REC_DELETED = 0", ("@id", role.Id)));
        Assert.True((await admin.CreateAsync(renamed.Name, Permissions.StockRead)).IsSuccess); // the name is free again
    }

    [Fact]
    public async Task A_role_can_start_as_a_copy_and_the_list_shows_the_system_roles_first_with_their_user_counts()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var clerk = (await admin.GetAsync("/api/roles")).Data<List<RoleDto>>().Single(r => r.Code == Roles.Clerk);

        var copy = (await admin.PostAsync("/api/roles", new RoleCreateRequest(Unique.Name("Clerk copy"), "Like a clerk.", clerk.Id))).Data<RoleDto>();

        Assert.Equal(clerk.Permissions, copy.Permissions);
        var list = (await admin.GetAsync("/api/roles")).Data<List<RoleDto>>();
        Assert.Equal([Roles.Admin, Roles.Clerk, Roles.Owner, Roles.Supervisor, Roles.WarehouseStaff], list.Where(r => r.IsSystem).Select(r => r.Code).Order(StringComparer.Ordinal));
        Assert.All(list.TakeWhile(r => r.IsSystem), r => Assert.True(r.IsSystem));
        Assert.True(list.Single(r => r.Code == Roles.Admin).UserCount >= 1);
        Assert.Equal(PermissionCatalog.Codes.Except(RoleMatrix.ModulePermissions), (await admin.GetAsync("/api/permissions")).Data<List<PermissionDto>>().Select(p => p.Code)); // the module permission is not offered while its module is off
    }

    [Theory]
    [InlineData("nope.read")]
    [InlineData("Users.Read")]   // matched exactly: another case is another (unknown) code
    [InlineData("users.read ")]
    public async Task A_code_that_is_not_in_the_catalog_exactly_is_refused(string code)
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var problem = (await admin.CreateAsync(Unique.Name("Role"), Permissions.StockRead, code)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Contains("Unknown permission", string.Join(' ', problem.Errors!.Values.SelectMany(v => v)));
    }

    [Fact]
    public async Task A_role_set_with_a_duplicate_a_missing_requirement_or_both_a_copy_and_a_list_is_refused_and_nothing_is_saved()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var before = (await admin.GetAsync("/api/roles?includeInactive=true")).Data<List<RoleDto>>().Count;

        (await admin.CreateAsync(Unique.Name("Role"), Permissions.StockRead, Permissions.StockRead)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await admin.CreateAsync(Unique.Name("Role"), Permissions.UsersManage)).ShouldBeProblem(HttpStatusCode.BadRequest);          // needs users.read
        (await admin.PostAsync("/api/roles", new RoleCreateRequest(Unique.Name("Role"), null, Guid.NewGuid(), [Permissions.StockRead]))).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/roles", new RoleCreateRequest(Unique.Name("Role"), null, Guid.NewGuid()))).ShouldBeProblem(HttpStatusCode.BadRequest); // no such role to copy
        (await admin.PostAsync("/api/roles", new RoleCreateRequest("x"))).ShouldBeProblem(HttpStatusCode.BadRequest);                  // name too short

        Assert.Equal(before, (await admin.GetAsync("/api/roles?includeInactive=true")).Data<List<RoleDto>>().Count);
    }

    [Fact]
    public async Task The_two_approval_tiers_need_nothing_from_each_other()
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var largeOnly = await NewRoleAsync(admin, Permissions.DocumentApproveLarge);   // like the Owner
        var standardOnly = await NewRoleAsync(admin, Permissions.DocumentApprove);
        var both = await NewRoleAsync(admin, Permissions.DocumentApprove, Permissions.DocumentApproveLarge);

        Assert.Equal([Permissions.DocumentApproveLarge], largeOnly.Permissions);
        Assert.Equal([Permissions.DocumentApprove], standardOnly.Permissions);
        Assert.Equal(2, both.Permissions.Count);
    }

    [Fact]
    public async Task A_name_that_is_taken_is_refused_whatever_its_case_and_a_new_code_never_collides_with_a_live_one()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var name = Unique.Name("Packer");
        var first = (await admin.CreateAsync(name)).Data<RoleDto>();

        (await admin.CreateAsync(name.ToUpperInvariant())).ShouldBeProblem(HttpStatusCode.Conflict);
        (await admin.CreateAsync(name + " ")).ShouldBeProblem(HttpStatusCode.Conflict);
        var other = (await admin.CreateAsync(Unique.Name("Other"))).Data<RoleDto>();
        Assert.Contains("already exists", (await admin.UpdateRoleAsync(other, name: name.ToLowerInvariant())).ShouldBeProblem(HttpStatusCode.Conflict).Detail);

        var lookAlike = (await admin.CreateAsync("a-dmin")).Data<RoleDto>();   // would make the code "ADmin", which the database takes for the Administrator role code "Admin"
        Assert.False(string.Equals(Roles.Admin, lookAlike.Code, StringComparison.OrdinalIgnoreCase));
        var sameLetters = (await admin.CreateAsync(name.Replace(' ', '-'))).Data<RoleDto>(); // another name with the same letters: the code gets a number
        Assert.NotEqual(first.Code, sameLetters.Code);
    }

    [Fact]
    public async Task The_system_roles_cannot_be_deleted_or_deactivated_and_the_administrator_role_cannot_be_changed()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var roles = (await admin.GetAsync("/api/roles")).Data<List<RoleDto>>();
        var clerk = roles.Single(r => r.Code == Roles.Clerk);
        var adminRole = roles.Single(r => r.Code == Roles.Admin);

        Assert.True(clerk.UserCount > 0);   // in use, so the order of the checks shows in the answer: the system role is refused for what it is, not for its users
        Assert.Equal(Messages.SystemRoleProtected, (await admin.DeleteRoleAsync(clerk)).ShouldBeProblem(HttpStatusCode.Conflict).Detail);
        Assert.Equal(Messages.SystemRoleProtected, (await admin.UpdateRoleAsync(clerk, active: false)).ShouldBeProblem(HttpStatusCode.Conflict).Detail);
        Assert.Equal(Messages.SystemRoleProtected, (await admin.UpdateRoleAsync(clerk, name: Unique.Name("Clerks"))).ShouldBeProblem(HttpStatusCode.Conflict).Detail);
        foreach (var other in roles.Where(r => r.Code != Roles.Admin && r.IsSystem))   // the whole administration core belongs to the Administrator role alone
            Assert.Equal(Messages.AdminCoreIsAdministratorOnly, (await admin.ReplaceAsync(other, [.. other.Permissions.Union(AdminCore)]))
                .ShouldBeProblem(HttpStatusCode.Conflict).Detail);

        Assert.Equal(PermissionCatalog.Codes.Except(RoleMatrix.ModulePermissions), (await admin.GetAsync($"/api/roles/{adminRole.Id}")).Data<RoleDto>().Permissions); // the Administrator role holds every code; one of a module that is off is kept but not shown
    }

    [Fact]
    public async Task A_permission_replace_needs_the_token_and_a_stale_token_is_a_conflict()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var role = await NewRoleAsync(admin, Permissions.StockRead);

        (await admin.PutAsync($"/api/roles/{role.Id}/permissions", new RolePermissionsRequest([Permissions.ReportsRead]))).ShouldBeProblem(HttpStatusCode.PreconditionRequired);
        (await admin.ReplaceAsync(role, [Permissions.ReportsRead], ifMatch: "not-the-token")).ShouldBeProblem(HttpStatusCode.Conflict);
        var current = (await admin.ReplaceAsync(role, [Permissions.StockRead, Permissions.ReportsRead])).Data<RolePermissionsResult>().Role;
        (await admin.ReplaceAsync(role, [Permissions.StockRead])).ShouldBeProblem(HttpStatusCode.Conflict);                 // the token the caller holds is now old
        (await admin.UpdateRoleAsync(role, name: Unique.Name("Late"), ifMatch: role.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(current.Permissions, (await admin.GetAsync($"/api/roles/{role.Id}")).Data<RoleDto>().Permissions);
    }

    [Fact]
    public async Task A_user_whose_role_row_is_gone_is_still_listed_and_can_be_moved_to_a_live_role()
    {
        // The API refuses to delete a role in use; this is the state a manual edit or an older release can leave. Such a user used to vanish from the list and answer 404 to a save.
        var admin = await fixture.AsAsync(Roles.Admin);
        var role = await NewRoleAsync(admin, Permissions.StockRead);
        var user = (await admin.Users.CreateAsync(NewUser(role.Code))).Data<UserDto>();
        await fixture.Database.ExecuteAsync("UPDATE [Role] SET REC_DELETED = 1 WHERE PublicId = @p", ("@p", role.Id));

        var listed = (await admin.GetAsync($"/api/users?search={user.Username}")).Data<Ims.Application.PagedResult<UserDto>>();
        Assert.Equal((1, role.Code), (listed.Total, Assert.Single(listed.Items).Role));
        Assert.Equal(user.WarehouseIds, listed.Items[0].WarehouseIds);
        (await admin.Users.UpdateAsync(user.Id, Move(user, role.Code))).ShouldBeProblem(HttpStatusCode.BadRequest);   // the deleted role cannot be given again
        Assert.Equal(Roles.Clerk, (await admin.Users.UpdateAsync(user.Id, Move(user, Roles.Clerk))).Data<UserDto>().Role);
        Assert.Equal(Roles.Clerk, (await admin.GetAsync($"/api/users?search={user.Username}")).Data<Ims.Application.PagedResult<UserDto>>().Items.Single().Role);
    }

    [Fact]
    public async Task A_role_that_users_hold_cannot_be_deleted_or_deactivated_and_the_refusal_counts_them_without_naming_them()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var role = await NewRoleAsync(admin, Permissions.StockRead);
        var first = (await admin.Users.CreateAsync(NewUser(role.Code))).Data<UserDto>();
        (await admin.Users.CreateAsync(NewUser(role.Code, active: false))).ShouldBeOk();    // an inactive user holds the role too
        Assert.Equal(2, (await admin.GetAsync($"/api/roles/{role.Id}")).Data<RoleDto>().UserCount);

        var delete = (await admin.DeleteRoleAsync(role)).ShouldBeProblem(HttpStatusCode.Conflict);
        var deactivate = (await admin.UpdateRoleAsync(role, active: false)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Contains("2 user(s)", delete.Detail);
        Assert.Contains("2 user(s)", deactivate.Detail);
        Assert.DoesNotContain(first.Username, delete.Detail + deactivate.Detail);
        Assert.True((await admin.GetAsync($"/api/roles/{role.Id}")).Data<RoleDto>().IsActive);

        foreach (var user in (await admin.GetAsync($"/api/users?role={role.Code}")).Data<PagedResult<UserDto>>().Items)
            (await admin.Users.UpdateAsync(user.Id, Move(user, Roles.Clerk))).ShouldBeOk();
        (await admin.DeleteRoleAsync(role)).ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_deleted_or_inactive_role_cannot_be_given_to_a_user()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var gone = await NewRoleAsync(admin, Permissions.StockRead);
        var inactive = await NewRoleAsync(admin, Permissions.StockRead);
        (await admin.DeleteRoleAsync(gone)).ShouldBe(HttpStatusCode.NoContent);
        (await admin.UpdateRoleAsync(inactive, active: false)).ShouldBeOk();

        (await admin.Users.CreateAsync(NewUser(gone.Code))).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await admin.Users.CreateAsync(NewUser(inactive.Code))).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.DoesNotContain((await admin.GetAsync("/api/roles?includeInactive=true")).Data<List<RoleDto>>(), r => r.Id == gone.Id);
    }

    [Fact]
    public async Task A_user_given_a_role_while_it_is_deleted_never_ends_up_holding_a_deleted_role()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        for (var round = 0; round < 6; round++)
        {
            var role = await NewRoleAsync(admin, Permissions.StockRead);
            var user = (await admin.Users.CreateAsync(NewUser(Roles.Clerk))).Data<UserDto>();

            await Concurrently.RunAsync<ApiResponse>([() => admin.DeleteRoleAsync(role), () => admin.Users.UpdateAsync(user.Id, Move(user, role.Code))]);

            // Either the delete won (the user kept the Clerk role) or the assignment won (the role is still there). Never a user holding a role that is gone.
            Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM [User] u JOIN Role r ON r.Id = u.RoleId WHERE u.PublicId = @u AND r.REC_DELETED = 1", ("@u", user.Id)));
        }
    }

    [Fact]
    public async Task A_role_edit_reaches_a_signed_in_person_at_the_next_refresh_not_before()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var role = await NewRoleAsync(admin, Permissions.StockRead, Permissions.ReportsRead);
        var request = NewUser(role.Code);
        (await admin.Users.CreateAsync(request)).ShouldBeOk();
        var person = fixture.CreateClient();
        var login = (await person.Auth.LoginAsync(request.Username, request.Password!)).Data<AuthResult>();
        person.AccessToken = login.AccessToken;
        (await person.GetAsync("/api/stock")).ShouldBeOk();

        (await admin.ReplaceAsync(role, [Permissions.ReportsRead])).ShouldBeOk();    // stock.read is taken away

        (await person.GetAsync("/api/stock")).ShouldBeOk();                          // the token in hand still says yes
        person.AccessToken = (await person.Auth.RefreshAsync(login.RefreshToken)).Data<AuthResult>().AccessToken;
        (await person.GetAsync("/api/stock")).ShouldBeProblem(HttpStatusCode.Forbidden); // the refreshed one does not
    }

    [Fact]
    public async Task An_update_that_leaves_out_the_description_or_the_state_keeps_them()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var made = (await admin.PostAsync("/api/roles", new RoleCreateRequest(Unique.Name("Keeper"), "Keeps things."))).Data<RoleDto>();

        var renamed = (await admin.PutAsync($"/api/roles/{made.Id}", new { name = Unique.Name("Keeper 2") })).Data<RoleDto>();

        Assert.Equal("Keeps things.", renamed.Description);
        Assert.True(renamed.IsActive);
    }

    [Theory]
    [InlineData("\u202E")]   // right-to-left override
    [InlineData("\u200B")]   // zero-width space
    [InlineData("\0")]
    public async Task A_name_or_description_with_control_or_direction_characters_is_refused(string bad)
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var role = await NewRoleAsync(admin);
        var name = Unique.Name("Night") + bad + "x";

        (await admin.PostAsync("/api/roles", new RoleCreateRequest(name))).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/roles", new RoleCreateRequest(Unique.Name("Night"), "line" + bad + "two"))).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await admin.UpdateRoleAsync(role, name: name)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await admin.PutAsync($"/api/roles/{role.Id}", new { name = role.Name, description = "line" + bad + "two" })).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_permission_list_longer_than_the_catalog_is_refused_whole_and_without_echo()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var role = await NewRoleAsync(admin);

        var answer = await admin.ReplaceAsync(role, Enumerable.Range(0, 20_000).Select(i => $"bad.code.{i}").ToArray());

        Assert.Equal(Messages.TooManyPermissions, answer.ShouldBeProblem(HttpStatusCode.BadRequest).Errors!.Values.Single().Single());
        Assert.DoesNotContain("bad.code.", answer.Body);
        Assert.True(answer.Bytes.Length < 2_000, $"{answer.Bytes.Length} bytes");
    }

    [Fact]
    public async Task A_short_list_names_ten_bad_codes_cut_to_64_characters_and_counts_the_rest()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var role = await NewRoleAsync(admin);
        var long1 = new string('x', 300);

        var answer = await admin.ReplaceAsync(role, [long1, .. Enumerable.Range(1, 14).Select(i => $"bad.code.{i}"), Permissions.StockRead]);   // 15 bad codes, within the catalog size

        var messages = answer.ShouldBeProblem(HttpStatusCode.BadRequest).Errors!.Values.SelectMany(m => m).ToList();
        Assert.Equal(11, messages.Count);                                                       // ten named, one count
        Assert.Contains(Messages.MorePermissionsRefused(5), messages);
        Assert.Contains(Messages.UnknownPermission(new string('x', 64) + "..."), messages);       // the long code is cut, not echoed whole
        Assert.DoesNotContain(long1, answer.Body);
    }
}

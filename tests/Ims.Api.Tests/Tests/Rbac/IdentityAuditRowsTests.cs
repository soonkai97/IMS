using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Roles;
using Ims.Application.Features.Users;
using Ims.Domain;
using Microsoft.Data.SqlClient;

namespace Ims.Api.Tests.Tests.Rbac;

/// <summary>
/// DDD-15, a characterization: the audit rows every role and user verb writes TODAY, pinned before the audit moves to domain events and a subscriber. Nothing here states a wish; it states what <c>main</c> does,
/// and the rows must stay byte for byte the same after the change.
/// <para>
/// What is compared: every row the caller wrote, read straight from the <c>AuditLogs</c> table in the order of its <c>Id</c>, as (action, entity type, entity id, before, after) with <c>Before</c> and <c>After</c>
/// as RAW strings (key order, nulls and booleans included). Each test is done by a person of its own, so the actor is unique and "every row of this actor since the mark" is exactly the trail of the verb. The business
/// rows (<c>role.created</c>, <c>user.updated</c> ...) and the row-level rows of the persistence layer (<c>created</c>, <c>updated</c>, <c>deleted</c> on Role, RolePermission and User) are in one trail, so the order
/// between them is pinned too. The internal ids the row-level rows carry are read back and put in; the generated row <c>Id</c> is used only for its order and <c>At</c> only to be a recent UTC time.
/// Also pinned: a refused write leaves no row, no password, hash or token ever appears in a row, and what a reader without any user or role permission gets of these rows.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public class IdentityAuditRowsTests(ApiFixture fixture)
{
    private const string Password = "Pw-Audit-Rows-1!";

    private readonly AccountFactory _accounts = new(fixture);

    private sealed record Pinned(string Actor, string Action, string EntityType, string EntityId, string? Before, string? After);

    private static Pinned Row(string actor, string action, string type, string id, string? before, string? after) => new(actor, action, type, id, before, after);

    private async Task<(TestAccount Who, ApiClient Client)> AdministratorAsync()
    {
        var account = await _accounts.CreateAsync(Roles.Admin);
        var (client, _) = await _accounts.SignInAsync(account);
        return (account, client);
    }

    private Task<long> MarkAsync() => fixture.Database.ScalarAsync<long>("SELECT ISNULL(MAX(Id), 0) FROM AuditLogs");

    private async Task<List<Pinned>> TrailAsync(long mark, string actor)
    {
        await using var connection = new SqlConnection(fixture.Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Actor, Action, EntityType, EntityId, [Before], [After], [At] FROM AuditLogs WHERE Id > @mark AND Actor = @actor ORDER BY Id";
        command.Parameters.AddWithValue("@mark", mark);
        command.Parameters.AddWithValue("@actor", actor);
        var rows = new List<Pinned>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Assert.True(Math.Abs((DateTime.UtcNow - reader.GetDateTime(6)).TotalMinutes) < 10, $"At {reader.GetDateTime(6):o} is not a recent UTC time");
            rows.Add(new Pinned(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return rows;
    }

    // ----- the internal ids the row-level rows carry (generated: read back, not normalised) -----
    private Task<int> RoleIdAsync(Guid publicId) => fixture.Database.ScalarAsync<int>("SELECT Id FROM [Role] WHERE PublicId = @p", ("@p", publicId));

    private Task<int> RoleIdOfCodeAsync(string code) => fixture.Database.ScalarAsync<int>("SELECT Id FROM [Role] WHERE Code = @c", ("@c", code));

    private Task<int> GrantIdAsync(int roleId, string code) => fixture.Database.ScalarAsync<int>("SELECT Id FROM RolePermission WHERE RoleId = @r AND PermissionCode = @c", ("@r", roleId), ("@c", code));

    private Task<int> UserIdAsync(Guid publicId) => fixture.Database.ScalarAsync<int>("SELECT Id FROM [User] WHERE PublicId = @p", ("@p", publicId));

    private Task<int> WarehouseIdAsync(Guid publicId) => fixture.Database.ScalarAsync<int>("SELECT Id FROM Warehouse WHERE PublicId = @p", ("@p", publicId));

    // ----- payload shapes, written out in full (raw JSON, key order as the code writes it today) -----
    private static string RoleRow(int id, string code, string description, bool active, string name, Guid publicId) =>
        $$"""{"Id":{{id}},"Code":"{{code}}","Description":"{{description}}","IsActive":{{Json(active)}},"IsSystem":false,"Name":"{{name}}","PublicId":"{{publicId}}"}""";

    private static string GrantRow(int id, string code, int roleId) => $$"""{"Id":{{id}},"PermissionCode":"{{code}}","RoleId":{{roleId}}}""";

    private static string Json(bool value) => value ? "true" : "false";

    private static string Quoted(IEnumerable<string> values) => "[" + string.Join(",", values.Select(v => $"\"{v}\"")) + "]";

    private static string UserSummary(string username, string role, bool allWarehouses, bool? active = null, IEnumerable<int>? warehouses = null) =>
        active is null
            ? $$"""{"Username":"{{username}}","Role":"{{role}}","AllWarehouses":{{Json(allWarehouses)}}}"""
            : $$"""{"Username":"{{username}}","Role":"{{role}}","AllWarehouses":{{Json(allWarehouses)}},"IsActive":{{Json(active.Value)}},"Warehouses":[{{string.Join(",", warehouses ?? [])}}]}""";

    private static UserSaveRequest Save(string username, string email, string role, bool active, string? password, bool? allWarehouses, params Guid[] warehouses) =>
        new(username, email, "Audit Rows", role, active, password, warehouses, allWarehouses);

    // ===== roles =====
    [Fact]
    public async Task Role_create_writes_the_business_row_first_with_the_permissions_in_catalog_order_then_the_row_of_the_role_and_one_row_per_grant_in_the_order_they_were_given()
    {
        var (who, client) = await AdministratorAsync();
        var name = Unique.Name("Audited");
        var mark = await MarkAsync();

        var role = (await client.CreateAsync(name, Permissions.StockRead, Permissions.MasterDataRead)).Data<RoleDto>();   // given against the catalog order

        var id = await RoleIdAsync(role.Id);
        var stock = await GrantIdAsync(id, Permissions.StockRead);
        var master = await GrantIdAsync(id, Permissions.MasterDataRead);
        Assert.Equal(
            [
                Row(who.Username, "role.created", "Role", role.Id.ToString(), null, $$"""{"Code":"{{role.Code}}","Name":"{{name}}","Permissions":{{Quoted([Permissions.MasterDataRead, Permissions.StockRead])}},"CopiedFrom":null}"""),
                Row(who.Username, "created", "Role", role.Id.ToString(), null, RoleRow(id, role.Code, "", true, name, role.Id)),
                Row(who.Username, "created", "RolePermission", stock.ToString(), null, GrantRow(stock, Permissions.StockRead, id)),
                Row(who.Username, "created", "RolePermission", master.ToString(), null, GrantRow(master, Permissions.MasterDataRead, id)),
            ],
            await TrailAsync(mark, who.Username));
        Assert.True(stock < master, "the grants were written in the order they were given");
    }

    [Fact]
    public async Task Role_copy_names_the_source_code_and_the_permissions_the_source_shows()
    {
        var (who, client) = await AdministratorAsync();
        var source = (await client.CreateAsync(Unique.Name("Source"), Permissions.MasterDataRead, Permissions.StockRead)).Data<RoleDto>();
        var name = Unique.Name("Copy");
        var mark = await MarkAsync();

        var copy = (await client.PostAsync("/api/roles", new RoleCreateRequest(name, "Copied text", source.Id))).Data<RoleDto>();

        var id = await RoleIdAsync(copy.Id);
        var master = await GrantIdAsync(id, Permissions.MasterDataRead);
        var stock = await GrantIdAsync(id, Permissions.StockRead);
        Assert.Equal(
            [
                Row(who.Username, "role.created", "Role", copy.Id.ToString(), null, $$"""{"Code":"{{copy.Code}}","Name":"{{name}}","Permissions":{{Quoted([Permissions.MasterDataRead, Permissions.StockRead])}},"CopiedFrom":"{{source.Code}}"}"""),
                Row(who.Username, "created", "Role", copy.Id.ToString(), null, RoleRow(id, copy.Code, "Copied text", true, name, copy.Id)),
                Row(who.Username, "created", "RolePermission", master.ToString(), null, GrantRow(master, Permissions.MasterDataRead, id)),
                Row(who.Username, "created", "RolePermission", stock.ToString(), null, GrantRow(stock, Permissions.StockRead, id)),
            ],
            await TrailAsync(mark, who.Username));
    }

    [Fact]
    public async Task Role_update_writes_a_business_row_with_the_text_and_state_before_and_after_even_when_nothing_changed_and_a_row_level_row_only_for_what_changed()
    {
        var (who, client) = await AdministratorAsync();
        var created = (await client.CreateAsync(Unique.Name("Before"), Permissions.MasterDataRead)).Data<RoleDto>();
        var renamed = Unique.Name("After");
        var mark = await MarkAsync();

        var changed = (await client.SendAsync(HttpMethod.Put, $"/api/roles/{created.Id}", new RoleUpdateRequest(renamed, "New text", false), r => r.Headers.TryAddWithoutValidation("If-Match", $"\"{created.ConcurrencyToken}\""))).Data<RoleDto>();
        var same = (await client.SendAsync(HttpMethod.Put, $"/api/roles/{created.Id}", new RoleUpdateRequest(renamed, "New text", false), r => r.Headers.TryAddWithoutValidation("If-Match", $"\"{changed.ConcurrencyToken}\""))).Data<RoleDto>();
        Assert.False(same.IsActive);

        Assert.Equal(
            [
                Row(who.Username, "role.updated", "Role", created.Id.ToString(), $$"""{"Name":"{{created.Name}}","Description":"","IsActive":true}""", $$"""{"Code":"{{created.Code}}","Name":"{{renamed}}","Description":"New text","IsActive":false}"""),
                Row(who.Username, "updated", "Role", created.Id.ToString(), $$"""{"Description":"","IsActive":true,"Name":"{{created.Name}}"}""", $$"""{"Description":"New text","IsActive":false,"Name":"{{renamed}}"}"""),
                Row(who.Username, "role.updated", "Role", created.Id.ToString(), $$"""{"Name":"{{renamed}}","Description":"New text","IsActive":false}""", $$"""{"Code":"{{created.Code}}","Name":"{{renamed}}","Description":"New text","IsActive":false}"""),
            ],
            await TrailAsync(mark, who.Username));
    }

    [Fact]
    public async Task Role_permission_replace_writes_what_was_removed_and_what_was_added_in_catalog_order_then_the_grant_rows_and_nothing_at_all_for_the_same_set_again()
    {
        var (who, client) = await AdministratorAsync();
        var created = (await client.CreateAsync(Unique.Name("Grants"), Permissions.MasterDataRead, Permissions.StockRead, Permissions.ReportsRead)).Data<RoleDto>();
        var id = await RoleIdAsync(created.Id);
        var stock = await GrantIdAsync(id, Permissions.StockRead);
        var reports = await GrantIdAsync(id, Permissions.ReportsRead);
        var mark = await MarkAsync();

        var replaced = await client.ReplaceAsync(created, [Permissions.MasterDataRead, Permissions.DocumentCreate, Permissions.MasterDataManage]);
        var token = (await client.GetAsync($"/api/roles/{created.Id}")).Data<RoleDto>();
        var again = await client.ReplaceAsync(token, [Permissions.MasterDataRead, Permissions.DocumentCreate, Permissions.MasterDataManage]);
        replaced.ShouldBeOk();
        again.ShouldBeOk();

        var create = await GrantIdAsync(id, Permissions.DocumentCreate);
        var manage = await GrantIdAsync(id, Permissions.MasterDataManage);
        var trail = await TrailAsync(mark, who.Username);
        Assert.Equal(
            [
                Row(who.Username, "role.permissions-changed", "Role", created.Id.ToString(), $$"""{"Code":"{{created.Code}}","Removed":{{Quoted([Permissions.StockRead, Permissions.ReportsRead])}}}""",
                    $$"""{"Code":"{{created.Code}}","Added":{{Quoted([Permissions.MasterDataManage, Permissions.DocumentCreate])}}}"""),
                Row(who.Username, "created", "RolePermission", manage.ToString(), null, GrantRow(manage, Permissions.MasterDataManage, id)),
                Row(who.Username, "created", "RolePermission", create.ToString(), null, GrantRow(create, Permissions.DocumentCreate, id)),
                Row(who.Username, "deleted", "RolePermission", stock.ToString(), GrantRow(stock, Permissions.StockRead, id), null),
                Row(who.Username, "deleted", "RolePermission", reports.ToString(), GrantRow(reports, Permissions.ReportsRead, id), null),
            ],
            // The two revoked grants come out in the order the change tracker lists them, which is not a rule of ours: they are compared as a pair, the rows before them in order.
            [.. trail.Take(3), .. trail.Skip(3).OrderBy(r => r.EntityId, StringComparer.Ordinal)]);
    }

    [Fact]
    public async Task Role_delete_writes_the_code_the_name_and_the_permissions_in_catalog_order_then_the_row_of_the_role_and_none_for_its_grants()
    {
        var (who, client) = await AdministratorAsync();
        var created = (await client.CreateAsync(Unique.Name("Gone"), Permissions.StockRead, Permissions.MasterDataRead)).Data<RoleDto>();
        var id = await RoleIdAsync(created.Id);
        var mark = await MarkAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteRoleAsync(created)).Status);

        Assert.Equal(
            [
                Row(who.Username, "role.deleted", "Role", created.Id.ToString(), $$"""{"Code":"{{created.Code}}","Name":"{{created.Name}}","Permissions":{{Quoted([Permissions.MasterDataRead, Permissions.StockRead])}}}""", null),
                Row(who.Username, "deleted", "Role", created.Id.ToString(), RoleRow(id, created.Code, "", true, created.Name, created.Id), null),
            ],
            await TrailAsync(mark, who.Username));
    }

    // ===== users =====
    [Fact]
    public async Task User_create_writes_the_row_of_the_user_first_and_the_business_row_after_it_and_neither_holds_the_password_or_its_hash()
    {
        var (who, client) = await AdministratorAsync();
        var username = Unique.Username("created");
        var warehouse = fixture.Reference.DefaultWarehouseId;
        var mark = await MarkAsync();

        var user = (await client.Users.CreateAsync(Save(username, Unique.Email(username), Roles.Clerk, true, Password, false, warehouse))).Data<UserDto>();

        var id = await UserIdAsync(user.Id);
        var trail = await TrailAsync(mark, who.Username);
        Assert.Equal(
            [
                Row(who.Username, "created", "User", user.Id.ToString(), null, $$"""{"Id":{{id}},"AllWarehouses":false,"IsActive":true,"PublicId":"{{user.Id}}","RoleId":{{await RoleIdOfCodeAsync(Roles.Clerk)}},"Username":"{{username}}"}"""),
                Row(who.Username, "user.created", "User", user.Id.ToString(), null, UserSummary(username, Roles.Clerk, false)),
            ],
            trail);
        await AssertNoSecretAsync(trail, user.Id, Password);
    }

    [Fact]
    public async Task User_update_writes_the_state_before_and_after_with_the_warehouses_as_internal_ids_and_a_row_level_row_only_for_what_changed()
    {
        var (who, client) = await AdministratorAsync();
        var username = Unique.Username("changed");
        var warehouse = fixture.Reference.DefaultWarehouseId;
        var user = (await client.Users.CreateAsync(Save(username, Unique.Email(username), Roles.Clerk, true, Password, false, warehouse))).Data<UserDto>();
        var warehouseId = await WarehouseIdAsync(warehouse);
        var clerk = await RoleIdOfCodeAsync(Roles.Clerk);
        var supervisor = await RoleIdOfCodeAsync(Roles.Supervisor);
        var mark = await MarkAsync();

        (await client.Users.UpdateAsync(user.Id, Save(username, Unique.Email(username), Roles.Supervisor, true, null, false, warehouse))).ShouldBeOk();             // a role change
        (await client.Users.UpdateAsync(user.Id, Save(username, Unique.Email(username), Roles.Supervisor, true, null, false, warehouse))).ShouldBeOk();             // nothing changes
        (await client.Users.UpdateAsync(user.Id, Save(username, Unique.Email(username), Roles.Supervisor, false, "Pw-Reset-Audit-1!", true))).ShouldBeOk();          // deactivated, scope widened, new password, no warehouses

        Assert.Equal(
            [
                Row(who.Username, "user.updated", "User", user.Id.ToString(), UserSummary(username, Roles.Clerk, false, true, [warehouseId]), UserSummary(username, Roles.Supervisor, false, true, [warehouseId])),
                Row(who.Username, "updated", "User", user.Id.ToString(), $$"""{"RoleId":{{clerk}}}""", $$"""{"RoleId":{{supervisor}}}"""),
                Row(who.Username, "user.updated", "User", user.Id.ToString(), UserSummary(username, Roles.Supervisor, false, true, [warehouseId]), UserSummary(username, Roles.Supervisor, false, true, [warehouseId])),
                Row(who.Username, "user.updated", "User", user.Id.ToString(), UserSummary(username, Roles.Supervisor, false, true, [warehouseId]), UserSummary(username, Roles.Supervisor, true, false, [])),
                Row(who.Username, "updated", "User", user.Id.ToString(), """{"AllWarehouses":false,"IsActive":true}""", """{"AllWarehouses":true,"IsActive":false}"""),
            ],
            await TrailAsync(mark, who.Username));
    }

    [Fact]
    public async Task A_new_password_never_reaches_a_row_not_the_password_not_the_hash_not_the_old_hash_and_a_reset_by_an_administrator_writes_no_password_row()
    {
        var (who, client) = await AdministratorAsync();
        var username = Unique.Username("secret");
        var user = (await client.Users.CreateAsync(Save(username, Unique.Email(username), Roles.Clerk, true, Password, false, fixture.Reference.DefaultWarehouseId))).Data<UserDto>();
        var oldHash = await fixture.Database.ScalarAsync<string>("SELECT PasswordHash FROM [User] WHERE PublicId = @p", ("@p", user.Id));
        var mark = await MarkAsync();

        (await client.Users.UpdateAsync(user.Id, Save(username, Unique.Email(username), Roles.Clerk, true, "Pw-Reset-Secret-1!", false, fixture.Reference.DefaultWarehouseId))).ShouldBeOk();

        var trail = await TrailAsync(mark, who.Username);
        Assert.Equal(["user.updated"], trail.Select(r => r.Action));   // the reset is one user.updated row (nothing the row level audits changed: the hash is not audited)
        var newHash = await fixture.Database.ScalarAsync<string>("SELECT PasswordHash FROM [User] WHERE PublicId = @p", ("@p", user.Id));
        Assert.NotEqual(oldHash, newHash);
        await AssertNoSecretAsync(trail, user.Id, "Pw-Reset-Secret-1!", oldHash, newHash);
    }

    [Fact]
    public async Task A_person_changing_their_own_password_writes_one_row_in_their_own_name_with_no_before_and_no_after_and_a_wrong_current_password_writes_none()
    {
        var account = await _accounts.CreateAsync(Roles.Clerk);
        var (client, _) = await _accounts.SignInAsync(account);
        var mark = await MarkAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/auth/change-password", new ChangePasswordRequest("not-the-password", "Pw-New-Own-1!"))).Status);
        Assert.Empty(await TrailAsync(mark, account.Username));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/change-password", new ChangePasswordRequest(account.Password, "Pw-New-Own-1!"))).Status);

        var trail = await TrailAsync(mark, account.Username);
        Assert.Equal([Row(account.Username, "user.password-changed", "User", account.Id.ToString(), null, null)], trail);
    }

    // ===== refusals and reading =====
    [Fact]
    public async Task A_refused_write_leaves_no_row_a_name_taken_a_role_in_use_a_closure_broken_and_a_person_locking_themselves_out()
    {
        var (who, client) = await AdministratorAsync();
        var taken = (await client.CreateAsync(Unique.Name("Taken"), Permissions.MasterDataRead)).Data<RoleDto>();
        var other = (await client.CreateAsync(Unique.Name("Other"), Permissions.MasterDataRead)).Data<RoleDto>();
        var holderName = Unique.Username("holder");
        (await client.Users.CreateAsync(Save(holderName, Unique.Email(holderName), other.Code, true, Password, false, fixture.Reference.DefaultWarehouseId))).ShouldBeOk();
        var mark = await MarkAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await client.UpdateRoleAsync(other, name: taken.Name)).Status);                       // the name is taken: after the text was changed in memory
        Assert.Equal(HttpStatusCode.Conflict, (await client.UpdateRoleAsync(other, active: false)).Status);                          // in use: after the text and the state were changed in memory
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteRoleAsync(other)).Status);                                         // in use: after the role was marked deleted in memory
        Assert.Equal(HttpStatusCode.BadRequest, (await client.ReplaceAsync(taken, [Permissions.MasterDataRead, Permissions.DocumentEditAny])).Status);   // document.edit-any needs document.create: after the grants were changed in memory
        Assert.Equal(HttpStatusCode.Conflict, (await client.Users.UpdateAsync(who.Id, Save(who.Username, who.Email, Roles.Admin, false, null, null))).Status);   // locking themselves out

        Assert.Empty(await TrailAsync(mark, who.Username));
    }

    [Fact]
    public async Task A_reader_without_any_user_or_role_permission_gets_these_rows_in_full_and_a_caller_without_the_audit_permission_gets_none()
    {
        var (who, client) = await AdministratorAsync();
        var name = Unique.Name("Visible");
        var role = (await client.CreateAsync(name, Permissions.MasterDataRead)).Data<RoleDto>();
        var username = Unique.Username("visible");
        var user = (await client.Users.CreateAsync(Save(username, Unique.Email(username), role.Code, true, Password, false, fixture.Reference.DefaultWarehouseId))).Data<UserDto>();

        // The owner holds audit.read and neither the users nor the roles permissions: no key of these rows is withheld from them (only cost and price keys are scrubbed), so the page equals the table.
        var owner = await fixture.AsAsync(Roles.Owner);
        foreach (var (id, type) in new[] { (role.Id, "Role"), (user.Id, "User") })
        {
            var page = (await owner.Audit.QueryAsync(entityId: id.ToString())).Data<AuditPage>();
            var stored = (await TrailAsync(0, who.Username)).Where(r => r.EntityId == id.ToString()).ToList();
            Assert.NotEmpty(stored);
            Assert.Equal(stored, page.Items.Reverse().Select(e => new Pinned(e.Actor, e.Action, e.EntityType, e.EntityId, e.Before, e.After)).ToList());
            Assert.All(page.Items, e => Assert.Equal(type, e.EntityType));
        }

        var clerk = await fixture.AsAsync(Roles.Clerk);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.Audit.QueryAsync(entityId: role.Id.ToString())).Status);
    }

    private async Task AssertNoSecretAsync(IEnumerable<Pinned> trail, Guid userId, params string[] secrets)
    {
        var hash = await fixture.Database.ScalarAsync<string>("SELECT PasswordHash FROM [User] WHERE PublicId = @p", ("@p", userId));
        var text = string.Join("\n", trail.Select(r => $"{r.Before}\n{r.After}"));
        foreach (var secret in secrets.Append(hash)) Assert.DoesNotContain(secret, text);
        Assert.DoesNotContain("PasswordHash", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", text.Replace("password-changed", ""), StringComparison.OrdinalIgnoreCase);
    }
}

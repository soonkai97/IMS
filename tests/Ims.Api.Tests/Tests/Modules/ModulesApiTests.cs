using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Modules;
using Ims.Application.Features.Settings;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Modules;

/// <summary>
/// PL1.1 acceptance: the module list and the switch (ADR modules-and-tracking-dimensions 4.2). A module with no row is off; an Administrator turns it on or off with a reason; every change
/// writes one audit event; a short reason, a missing state or an unknown code changes nothing; nobody else may flip; and the list the web reads at sign-in follows at once. The tests
/// start and end with no <c>Module.*</c> row, so they leave the shared database as they found it.
/// </summary>
[Collection(ApiCollection.Name)]
public class ModulesApiTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Accounting = ModuleCodes.Accounting;
    private const string Key = "Module.accounting";

    private int _auditBaseline;

    public async Task InitializeAsync()
    {
        await ResetAsync();
        _auditBaseline = await RawAuditRowsAsync(); // the audit log is append-only: count what this test adds
    }

    public Task DisposeAsync() => ResetAsync();

    private Task ResetAsync() => Ims.Api.Tests.Tests.Purchasing.PurchasingScenario.ResetAsync(fixture); // also closes any order another test left open: the purchasing check refuses to switch off over one

    private static async Task<bool> AccountingEnabledAsync(ApiClient admin) => (await admin.Modules.ListAsync()).Data<List<ModuleDto>>().Single(m => m.Code == Accounting).Enabled;

    private static string Why() => $"Pilot start {Guid.NewGuid():N}"[..24];

    private Task<int> RawAuditRowsAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE [Action] LIKE N'module.%'");

    private async Task<int> ModuleAuditRowsAsync() => await RawAuditRowsAsync() - _auditBaseline;

    private Task<int> ModuleRowsAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SystemSetting WHERE [Key] LIKE N'Module.%'");

    private Task<string?> StoredAsync() => fixture.Database.ScalarAsync<string?>("SELECT [Value] FROM SystemSetting WHERE [Key] = @k", ("@k", Key));

    private async Task<IReadOnlyList<AuditEntry>> EventsAsync(string action, string reason)
    {
        var page = (await (await fixture.AsAsync(Roles.Admin)).Audit.QueryAsync(action: action, entityId: Key)).Data<AuditPage>();
        return page.Items.Where(e => e.After is not null && e.After.Contains(reason, StringComparison.Ordinal)).ToList();
    }

    private static string Field(string? json, string name)
    {
        using var doc = JsonDocument.Parse(json!);
        return doc.RootElement.GetProperty(name).ToString();
    }

    // ----- the list -----
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public async Task With_no_module_row_every_signed_in_role_sees_accounting_and_purchasing_listed_as_off(string role)
    {
        var list = (await (await fixture.AsAsync(role)).Modules.ListAsync()).Data<List<ModuleDto>>();

        Assert.Equal([Accounting, ModuleCodes.Purchasing], list.Select(m => m.Code));
        Assert.All(list, module =>
        {
            Assert.False(module.Enabled);
            Assert.False(string.IsNullOrWhiteSpace(module.Name));
            Assert.Empty(module.Requires);
        });
        Assert.Equal(0, await ModuleRowsAsync()); // listing writes nothing
    }

    [Fact]
    public async Task An_anonymous_caller_gets_401_on_the_list_and_on_the_switch()
    {
        var anonymous = fixture.Anonymous();

        (await anonymous.Modules.ListAsync()).ShouldBeProblem(HttpStatusCode.Unauthorized);
        (await anonymous.Modules.SwitchAsync(Accounting, true, Why())).ShouldBeProblem(HttpStatusCode.Unauthorized);
        Assert.Equal(0, await ModuleRowsAsync());
    }

    // ----- the switch -----
    [Fact]
    public async Task The_administrator_turns_the_module_on_and_the_next_list_shows_it_and_one_audit_event_holds_who_why_before_and_after()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var reason = Why();

        var answer = (await admin.Modules.SwitchAsync(Accounting, true, reason)).Data<ModuleDto>();

        Assert.Equal(Accounting, answer.Code);
        Assert.True(answer.Enabled);
        Assert.Equal("true", await StoredAsync());
        Assert.True(await AccountingEnabledAsync(admin));
        var events = await EventsAsync(AuditActions.ModuleEnabled, reason);
        var enabled = Assert.Single(events);
        Assert.Equal(TestUsers.UsernameOf(Roles.Admin), enabled.Actor);
        Assert.Equal(Key, enabled.EntityId);
        Assert.Equal("False", Field(enabled.Before, "Enabled"));
        Assert.Equal("True", Field(enabled.After, "Enabled"));
        Assert.Equal(reason, Field(enabled.After, "Reason"));
    }

    [Fact]
    public async Task Turning_it_off_again_is_audited_as_disabled_with_before_on_and_after_off_and_the_reason_is_trimmed()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        (await admin.Modules.SwitchAsync(Accounting, true, Why())).ShouldBeOk();
        var reason = Why();

        var answer = (await admin.Modules.SwitchAsync(Accounting, false, $"  {reason}  ")).Data<ModuleDto>();

        Assert.False(answer.Enabled);
        Assert.Equal("false", await StoredAsync());
        Assert.False(await AccountingEnabledAsync(admin));
        var disabled = Assert.Single(await EventsAsync(AuditActions.ModuleDisabled, reason));
        Assert.Equal("True", Field(disabled.Before, "Enabled"));
        Assert.Equal("False", Field(disabled.After, "Enabled"));
        Assert.Equal(reason, Field(disabled.After, "Reason"));
    }

    [Fact]
    public async Task Asking_for_the_state_it_already_has_is_a_200_that_changes_nothing_and_writes_no_event()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        (await admin.Modules.SwitchAsync(Accounting, false, Why())).ShouldBeOk(); // already off, and there is no row
        Assert.Equal(0, await ModuleRowsAsync());
        Assert.Equal(0, await ModuleAuditRowsAsync());
        (await admin.Modules.SwitchAsync(Accounting, true, Why())).ShouldBeOk();
        var events = await ModuleAuditRowsAsync();

        var again = Why();
        (await admin.Modules.SwitchAsync(Accounting, true, again)).ShouldBeOk();

        Assert.Equal(events, await ModuleAuditRowsAsync());
        Assert.Empty(await EventsAsync(AuditActions.ModuleEnabled, again));
        Assert.Equal("true", await StoredAsync());
    }

    [Theory]
    [InlineData("abcd")]
    [InlineData("    ")]
    [InlineData("")]
    public async Task A_reason_of_fewer_than_five_characters_is_a_400_and_nothing_changes_or_is_audited(string reason)
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        var problem = (await admin.Modules.SwitchAsync(Accounting, true, reason)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.NotEmpty(problem.ErrorsFor("Reason"));
        Assert.Equal(0, await ModuleRowsAsync());
        Assert.Equal(0, await ModuleAuditRowsAsync());
    }

    [Fact]
    public async Task A_body_without_the_state_is_a_400_and_never_a_quiet_switch_off()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        (await admin.Modules.SwitchAsync(Accounting, true, Why())).ShouldBeOk();
        var events = await ModuleAuditRowsAsync();

        var missing = await admin.Modules.SwitchAsync(Accounting, null, Why());
        var empty = await admin.PutAsync($"/api/modules/{Accounting}", new { });

        Assert.NotEmpty(missing.ShouldBeProblem(HttpStatusCode.BadRequest).ErrorsFor("Enabled"));
        empty.ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal("true", await StoredAsync());
        Assert.Equal(events, await ModuleAuditRowsAsync());
    }

    [Theory]
    [InlineData("batch")]
    [InlineData("Accounting")]
    [InlineData("modules")]
    public async Task An_unknown_code_is_a_404_and_creates_no_row_and_no_event(string code)
    {
        var admin = await fixture.AsAsync(Roles.Admin);

        (await admin.Modules.SwitchAsync(code, true, Why())).ShouldBeProblem(HttpStatusCode.NotFound);

        Assert.Equal(0, await ModuleRowsAsync());
        Assert.Equal(0, await ModuleAuditRowsAsync());
    }

    // ----- who may -----
    [Theory]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public async Task Only_the_administrator_holds_the_permission_so_every_other_role_gets_403_and_nothing_changes(string role)
    {
        var client = await fixture.AsAsync(role);

        (await client.Modules.SwitchAsync(Accounting, true, Why())).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Equal(0, await ModuleRowsAsync());
        Assert.Equal(0, await ModuleAuditRowsAsync());
    }

    [Fact]
    public async Task A_token_that_names_the_Administrator_role_but_lacks_the_permission_is_403_because_the_claim_decides_not_the_role()
    {
        var token = ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: PermissionCatalog.Codes.Except([Permissions.ModulesManage]));

        (await fixture.CreateClient(token).Modules.SwitchAsync(Accounting, true, Why())).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Equal(0, await ModuleRowsAsync());
    }

    [Fact]
    public async Task A_token_that_holds_the_permission_but_is_signed_with_another_key_is_401()
    {
        var foreignKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var token = ForgedTokens.Create(foreignKey, Roles.Admin, permissions: PermissionCatalog.Codes);

        var client = fixture.CreateClient(token);

        (await client.Modules.SwitchAsync(Accounting, true, Why())).ShouldBeProblem(HttpStatusCode.Unauthorized);
        (await client.Modules.ListAsync()).ShouldBeProblem(HttpStatusCode.Unauthorized);
        Assert.Equal(0, await ModuleRowsAsync());
    }

    // ----- what the web reads -----
    [Fact]
    public async Task Sign_in_refresh_and_me_report_the_modules_that_are_on_and_a_flip_shows_on_the_very_next_call()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var client = fixture.CreateClient();
        var login = (await client.Auth.LoginAsync(TestUsers.UsernameOf(Roles.Clerk), fixture.PasswordOf(Roles.Clerk))).Data<AuthResult>();
        client.AccessToken = login.AccessToken;
        Assert.Empty(login.User.Modules);
        Assert.Empty((await client.Auth.MeAsync()).Data<UserInfo>().Modules);

        (await admin.Modules.SwitchAsync(Accounting, true, Why())).ShouldBeOk();

        Assert.Equal([Accounting], (await client.Auth.MeAsync()).Data<UserInfo>().Modules); // the token in hand is the old one: the list is read, not carried
        var refreshed = (await fixture.CreateClient().Auth.RefreshAsync(login.RefreshToken)).Data<AuthResult>();
        Assert.Equal([Accounting], refreshed.User.Modules);
        var second = (await fixture.CreateClient().Auth.LoginAsync(TestUsers.UsernameOf(Roles.Supervisor), fixture.PasswordOf(Roles.Supervisor))).Data<AuthResult>();
        Assert.Equal([Accounting], second.User.Modules);

        (await admin.Modules.SwitchAsync(Accounting, false, Why())).ShouldBeOk();

        Assert.Empty((await client.Auth.MeAsync()).Data<UserInfo>().Modules);
    }

    [Fact]
    public async Task A_row_for_a_module_the_catalog_does_not_know_and_a_value_that_is_not_exactly_true_leave_everything_off()
    {
        await fixture.Database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (N'Module.ghost', N'true')");
        var admin = await fixture.AsAsync(Roles.Admin);
        Assert.False(await AccountingEnabledAsync(admin));
        Assert.Empty((await admin.Auth.MeAsync()).Data<UserInfo>().Modules);

        await fixture.Database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (@k, N'True')", ("@k", Key));

        Assert.False(await AccountingEnabledAsync(admin));
    }

    [Fact]
    public async Task The_settings_endpoints_neither_show_nor_touch_the_module_rows()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        (await admin.Modules.SwitchAsync(Accounting, true, Why())).ShouldBeOk();
        var settings = (await admin.Settings.GetAsync()).Data<SettingsDto>();

        (await admin.Settings.UpdateAsync(settings)).ShouldBeOk();

        Assert.Equal(settings, (await admin.Settings.GetAsync()).Data<SettingsDto>());
        Assert.Equal("true", await StoredAsync());
        Assert.True(await AccountingEnabledAsync(admin));
    }

    // ----- races -----
    [Fact]
    public async Task Simultaneous_flips_to_the_same_state_all_answer_200_and_the_audit_shows_one_change_with_the_right_before_and_after()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var reasons = Enumerable.Range(0, 8).Select(_ => Why()).ToList();

        var on = await Concurrently.RunAsync(reasons.Select(r => (Func<Task<ApiResponse>>)(() => admin.Modules.SwitchAsync(Accounting, true, r))));

        Assert.All(on, r => r.ShouldBeOk());
        var enabledEvents = (await Task.WhenAll(reasons.Select(r => EventsAsync(AuditActions.ModuleEnabled, r)))).SelectMany(e => e).ToList();
        var change = Assert.Single(enabledEvents);
        Assert.Equal("False", Field(change.Before, "Enabled"));
        Assert.Equal("True", Field(change.After, "Enabled"));

        var offReasons = Enumerable.Range(0, 8).Select(_ => Why()).ToList();
        var off = await Concurrently.RunAsync(offReasons.Select(r => (Func<Task<ApiResponse>>)(() => admin.Modules.SwitchAsync(Accounting, false, r))));

        Assert.All(off, r => r.ShouldBeOk());
        var disabledEvents = (await Task.WhenAll(offReasons.Select(r => EventsAsync(AuditActions.ModuleDisabled, r)))).SelectMany(e => e).ToList();
        var back = Assert.Single(disabledEvents);
        Assert.Equal("True", Field(back.Before, "Enabled"));
        Assert.Equal("False", Field(back.After, "Enabled"));
        Assert.Equal("false", await StoredAsync());
        Assert.Equal(2, await ModuleAuditRowsAsync());
    }
}

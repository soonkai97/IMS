using System.Net;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Modules;
using Ims.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Modules;

/// <summary>
/// Plan risk 11: until PL1.8 ships, the API itself refuses to turn <c>accounting</c> on, not only the web. The shared test host allows it (the other module tests need it on); these tests
/// run on a host of their own that has the production list, and check that the refusal is a 400 before anything is written or audited, that turning off still works, and that
/// the production registration is the catalog list. PL1.8 deletes this class with the list.
/// </summary>
[Collection(ApiCollection.Name)]
public class ModuleNotYetSwitchableTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Accounting = ModuleCodes.Accounting;
    private const string Key = "Module.accounting";

    private WebApplicationFactory<Program> _host = null!;
    private ApiClient _admin = null!;
    private int _auditBaseline;

    public async Task InitializeAsync()
    {
        await ResetAsync();
        _host = fixture.CreateHost(b => b.ConfigureTestServices(s => s.AddSingleton(new NotYetSwitchableModules(ModuleCatalog.NotYetSwitchableOn))));
        _admin = await SignInAsync(_host);
        _auditBaseline = await AuditRowsAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await ResetAsync();
    }

    private Task ResetAsync() => fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] LIKE N'Module.%'");

    private async Task<ApiClient> SignInAsync(WebApplicationFactory<Program> host)
    {
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = (await client.Auth.LoginAsync(TestUsers.UsernameOf(Roles.Admin), fixture.PasswordOf(Roles.Admin))).Data<Ims.Application.Features.Auth.AuthResult>().AccessToken;
        return client;
    }

    private static string Why() => $"Pilot start {Guid.NewGuid():N}"[..24];

    private Task<int> AuditRowsAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE [Action] LIKE N'module.%'");

    private async Task<int> NewAuditRowsAsync() => await AuditRowsAsync() - _auditBaseline;

    private Task<string?> StoredAsync() => fixture.Database.ScalarAsync<string?>("SELECT [Value] FROM SystemSetting WHERE [Key] = @k", ("@k", Key));

    private Task<int> ModuleRowsAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SystemSetting WHERE [Key] LIKE N'Module.%'");

    [Fact]
    public async Task Turning_accounting_on_is_a_400_with_a_plain_sentence_and_changes_and_audits_nothing()
    {
        var problem = (await _admin.Modules.SwitchAsync(Accounting, true, Why())).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(Messages.ModuleNotYetSwitchableOn, problem.Detail);
        Assert.Equal(0, await ModuleRowsAsync());
        Assert.Equal(0, await NewAuditRowsAsync());
        Assert.False(Assert.Single((await _admin.Modules.ListAsync()).Data<List<ModuleDto>>()).Enabled);
    }

    [Fact]
    public async Task A_row_that_says_off_stays_off_and_one_that_says_on_is_not_confirmed_either()
    {
        await fixture.Database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (@k, N'false')", ("@k", Key));
        (await _admin.Modules.SwitchAsync(Accounting, true, Why())).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal("false", await StoredAsync());

        await fixture.Database.ExecuteAsync("UPDATE SystemSetting SET [Value] = N'true' WHERE [Key] = @k", ("@k", Key));
        (await _admin.Modules.SwitchAsync(Accounting, true, Why())).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal("true", await StoredAsync());
        Assert.Equal(0, await NewAuditRowsAsync());
    }

    [Fact]
    public async Task Turning_accounting_off_is_still_allowed_and_audited_and_off_when_already_off_is_a_quiet_200()
    {
        await fixture.Database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (@k, N'true')", ("@k", Key)); // an installation that switched it on before the gate
        var reason = Why();

        var answer = (await _admin.Modules.SwitchAsync(Accounting, false, reason)).Data<ModuleDto>();

        Assert.False(answer.Enabled);
        Assert.Equal("false", await StoredAsync());
        Assert.Equal(1, await NewAuditRowsAsync());

        (await _admin.Modules.SwitchAsync(Accounting, false, Why())).ShouldBeOk();
        Assert.Equal(1, await NewAuditRowsAsync());
    }

    [Fact]
    public async Task A_module_that_is_not_on_the_list_is_switched_on_as_before()
    {
        await using var host = fixture.CreateHost(b => b.ConfigureTestServices(s => s.AddSingleton(new NotYetSwitchableModules(["some-other-module"]))));
        var admin = await SignInAsync(host);

        (await admin.Modules.SwitchAsync(Accounting, true, Why())).ShouldBeOk();

        Assert.Equal("true", await StoredAsync());
        Assert.Equal(1, await NewAuditRowsAsync());
    }

    [Fact]
    public async Task An_unknown_code_is_still_a_404_and_a_short_reason_still_a_400_that_names_the_reason()
    {
        (await _admin.Modules.SwitchAsync("batch", true, Why())).ShouldBeProblem(HttpStatusCode.NotFound);
        var problem = (await _admin.Modules.SwitchAsync(Accounting, true, "abc")).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.NotEmpty(problem.ErrorsFor("Reason"));
    }

    [Fact]
    public void The_production_registration_is_the_catalog_list_and_names_accounting()
    {
        using var provider = new ServiceCollection().AddApplication().BuildServiceProvider();

        var registered = provider.GetRequiredService<NotYetSwitchableModules>();

        Assert.Same(ModuleCatalog.NotYetSwitchableOn, registered.Codes);
        Assert.Contains(Accounting, registered.Codes);
    }
}

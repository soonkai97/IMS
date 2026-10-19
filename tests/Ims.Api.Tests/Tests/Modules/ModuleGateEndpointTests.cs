using System.Net;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Modules;

/// <summary>
/// PL1.1 acceptance 5 and 7: the gate. While a module is off, every signed-in caller of its endpoints gets the 404 of an unknown route, whatever their permissions, so a caller cannot probe
/// for it; no token is still 401; with the module on the permission policy decides (403 or 200); a flip works on the very next request; a module the catalog does not know is off; the core
/// is never gated. The endpoints are the probe controller of this test host (see <see cref="ModuleProbeController"/>).
/// </summary>
[Collection(ApiCollection.Name)]
public class ModuleGateEndpointTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Reason = "Gate test";
    private WebApplicationFactory<Program> _host = null!;
    private ApiClient _admin = null!;

    public async Task InitializeAsync()
    {
        await ResetAsync();
        _host = fixture.CreateHost(b => b.ConfigureTestServices(s => s.AddControllers().AddApplicationPart(typeof(ModuleProbeController).Assembly)));
        _admin = await fixture.AsAsync(Roles.Admin);
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await ResetAsync();
    }

    private Task ResetAsync() => fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] LIKE N'Module.%'");

    private ApiClient Caller(string? token = null)
    {
        var client = ApiFixture.CreateClient(_host);
        client.AccessToken = token;
        return client;
    }

    private string WithoutThePermission() => ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: PermissionCatalog.Codes.Except([Permissions.StockRead]));

    private async Task<string> PermittedAsync(string role = Roles.WarehouseStaff) => (await fixture.AsAsync(role)).AccessToken!;

    private Task<ApiResponse> Probe(ApiClient client, string endpoint) => client.GetAsync($"/api/probe/{endpoint}");

    [Fact]
    public async Task While_the_module_is_off_every_signed_in_caller_gets_404_with_or_without_the_permission_and_an_anonymous_caller_still_gets_401()
    {
        foreach (var token in new[] { WithoutThePermission(), await PermittedAsync(Roles.WarehouseStaff), await PermittedAsync(Roles.Admin), await PermittedAsync(Roles.Clerk) })
            (await Probe(Caller(token), "accounting")).ShouldBeProblem(HttpStatusCode.NotFound);

        (await Probe(Caller(), "accounting")).ShouldBeProblem(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_404_of_an_off_module_is_the_same_problem_as_the_404_of_a_route_that_does_not_exist()
    {
        var caller = Caller(await PermittedAsync());

        var gated = (await Probe(caller, "accounting")).ShouldBeProblem(HttpStatusCode.NotFound);
        var unknown = (await Probe(caller, "no-such-endpoint")).ShouldBeProblem(HttpStatusCode.NotFound);

        Assert.Equal(unknown.Title, gated.Title);
        Assert.Equal(unknown.Detail, gated.Detail);
        Assert.Equal(unknown.Status, gated.Status);
    }

    [Fact]
    public async Task While_the_module_is_on_the_permission_decides_403_without_it_and_200_with_it_and_401_with_no_token()
    {
        (await _admin.Modules.SwitchAsync(ModuleCodes.Accounting, true, Reason)).ShouldBeOk();

        (await Probe(Caller(WithoutThePermission()), "accounting")).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await Probe(Caller(await PermittedAsync()), "accounting")).ShouldBeOk();
        (await Probe(Caller(await PermittedAsync(Roles.Admin)), "accounting")).ShouldBeOk();
        (await Probe(Caller(), "accounting")).ShouldBeProblem(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_flip_works_on_the_very_next_request_in_both_directions_with_the_same_token()
    {
        var caller = Caller(await PermittedAsync());
        (await Probe(caller, "accounting")).ShouldBe(HttpStatusCode.NotFound);

        (await _admin.Modules.SwitchAsync(ModuleCodes.Accounting, true, Reason)).ShouldBeOk();
        (await Probe(caller, "accounting")).ShouldBeOk();

        (await _admin.Modules.SwitchAsync(ModuleCodes.Accounting, false, Reason)).ShouldBeOk();
        (await Probe(caller, "accounting")).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_module_the_catalog_does_not_know_is_off_for_everyone_even_when_a_row_says_true()
    {
        await fixture.Database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (N'Module.unregistered', N'true')");
        var caller = Caller(await PermittedAsync(Roles.Admin));

        (await Probe(caller, "unregistered")).ShouldBeProblem(HttpStatusCode.NotFound);
        (await Probe(Caller(), "unregistered")).ShouldBeProblem(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_endpoint_that_allows_anonymous_callers_is_still_gated_404_while_the_module_is_off_for_everyone_and_open_when_it_is_on()
    {
        (await Probe(Caller(), "anonymous")).ShouldBeProblem(HttpStatusCode.NotFound);
        (await Probe(Caller(await PermittedAsync()), "anonymous")).ShouldBeProblem(HttpStatusCode.NotFound);

        (await _admin.Modules.SwitchAsync(ModuleCodes.Accounting, true, Reason)).ShouldBeOk();

        (await Probe(Caller(), "anonymous")).ShouldBeOk();
    }

    [Fact]
    public async Task The_core_is_never_gated_with_every_module_off_or_on()
    {
        var caller = Caller(await PermittedAsync());

        (await Probe(caller, "core")).ShouldBeOk();

        (await _admin.Modules.SwitchAsync(ModuleCodes.Accounting, true, Reason)).ShouldBeOk();
        (await Probe(caller, "core")).ShouldBeOk();
    }
}

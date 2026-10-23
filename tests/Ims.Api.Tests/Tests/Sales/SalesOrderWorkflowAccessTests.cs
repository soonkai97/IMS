using System.Net;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Sales;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// PL3.3: who gets through the door of each transition. While the module is off every transition is a 404 for every signed-in caller (401 without a token); with it on a token that is forged
/// with exactly the permissions it names is let in or refused by those permissions alone, whatever role it claims: submit and recall need so.manage; approve and reject need an approval tier and so.read;
/// cancel needs so.manage or an approval tier, and so.read.
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesOrderWorkflowAccessTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _p = null!;

    public async Task InitializeAsync() => _p = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private ApiClient Forged(params string[] permissions) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    private static readonly string[] Actions = ["submit", "approve", "reject", "recall", "cancel"];

    private static Func<ApiClient, Guid, Task<ApiResponse>> Call(string action) => action switch
    {
        "submit" => (c, id) => c.SalesOrders.SubmitAsync(id),
        "approve" => (c, id) => c.SalesOrders.ApproveAsync(id),
        "reject" => (c, id) => c.SalesOrders.RejectAsync(id, "a reason that is long enough"),
        "recall" => (c, id) => c.SalesOrders.RecallAsync(id),
        _ => (c, id) => c.SalesOrders.CancelAsync(id, "a reason that is long enough")
    };

    [Fact]
    public async Task While_the_module_is_off_every_transition_is_404_for_every_signed_in_caller_and_401_for_no_token_and_nothing_changes()
    {
        var submitted = await _p.SubmittedWorthAsync(6000m);
        await _p.TurnOffAsync();
        var before = await fixture.Database.ScalarAsync<string>("SELECT CONVERT(varchar(40), ROW_VERSION, 1) FROM SalesOrder WHERE PublicId = @id", ("@id", submitted.Id));

        foreach (var action in Actions)
        {
            foreach (var who in new[] { _p.Admin, _p.Owner, _p.Supervisor, _p.Clerk, _p.Staff, Forged(PermissionCatalog.Codes.ToArray()), Forged() })
                (await Call(action)(who, submitted.Id)).ShouldBeProblem(HttpStatusCode.NotFound);
            Assert.Equal(401, (await Call(action)(fixture.Anonymous(), submitted.Id)).StatusCode);
        }

        Assert.Equal(before, await fixture.Database.ScalarAsync<string>("SELECT CONVERT(varchar(40), ROW_VERSION, 1) FROM SalesOrder WHERE PublicId = @id", ("@id", submitted.Id)));
    }

    [Theory]
    [InlineData("submit", true)]
    [InlineData("recall", true)]
    [InlineData("approve", false)]
    [InlineData("reject", false)]
    public async Task Without_po_manage_a_token_that_holds_everything_else_cannot_submit_or_recall(string action, bool needsSoManage)
    {
        var submitted = await _p.SubmittedWorthAsync(6000m);
        var token = Forged(PermissionCatalog.Codes.Except([Permissions.SoManage]).ToArray());

        var response = await Call(action)(token, submitted.Id);

        if (needsSoManage) response.ShouldBeProblem(HttpStatusCode.Forbidden);
        else Assert.NotEqual(HttpStatusCode.Forbidden, response.Status); // the review tiers open approve, reject and cancel without so.manage
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("cancel")]
    public async Task Without_po_read_a_token_that_holds_everything_else_is_403_even_with_both_approval_tiers_and_po_manage(string action)
    {
        var submitted = await _p.SubmittedWorthAsync(6000m);

        (await Call(action)(Forged(PermissionCatalog.Codes.Except([Permissions.SoRead]).ToArray()), submitted.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Equal(SalesOrderStatus.Submitted, (await _p.ReadAsync(submitted.Id)).Status);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task Without_either_approval_tier_a_token_with_po_read_and_po_manage_cannot_approve_or_reject(string action)
    {
        var submitted = await _p.SubmittedWorthAsync(6000m);
        var token = Forged(PermissionCatalog.Codes.Except([Permissions.DocumentApprove, Permissions.DocumentApproveLarge]).ToArray());

        (await Call(action)(token, submitted.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Equal(SalesOrderStatus.Submitted, (await _p.ReadAsync(submitted.Id)).Status);
    }

    [Theory]
    [InlineData("submit")]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("recall")]
    [InlineData("cancel")]
    public async Task A_token_with_po_read_alone_is_403_on_every_transition(string action)
    {
        var submitted = await _p.SubmittedWorthAsync(6000m);

        (await Call(action)(Forged(Permissions.SoRead), submitted.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Equal(SalesOrderStatus.Submitted, (await _p.ReadAsync(submitted.Id)).Status);
    }

    [Fact]
    public async Task One_approval_tier_with_po_read_is_enough_to_approve_and_the_tier_of_the_order_decides_the_rest()
    {
        var standard = await _p.SubmittedWorthAsync(6000m);
        var large = await _p.SubmittedWorthAsync(25000m);
        var standardOnly = Forged(Permissions.SoRead, Permissions.DocumentApprove, Permissions.SoManage);
        var largeOnly = Forged(Permissions.SoRead, Permissions.DocumentApproveLarge, Permissions.SoManage);

        (await standardOnly.SalesOrders.ApproveAsync(large.Id, large.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await largeOnly.SalesOrders.ApproveAsync(standard.Id, standard.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);
        Assert.Equal(SalesOrderStatus.Approved, (await standardOnly.SalesOrders.ApproveAsync(standard.Id, standard.ConcurrencyToken)).Data<SalesOrderDto>().Status);
        Assert.Equal(SalesOrderStatus.Approved, (await largeOnly.SalesOrders.ApproveAsync(large.Id, large.ConcurrencyToken)).Data<SalesOrderDto>().Status);
    }
}

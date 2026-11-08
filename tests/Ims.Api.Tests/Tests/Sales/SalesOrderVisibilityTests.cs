using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Sales;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// A selling price is gated by <c>so.manage</c>, never by <c>cost.view</c>: the rule numbers of an approval trigger (the total at selling price and the threshold it reached) and the amount in the
/// audit trail follow it. A reader with so.read and cost.view but not so.manage sees neither; one with so.manage (and cost.view for the shared trigger contract) sees both. (PL3.2 and PL3.3 review.)
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesOrderVisibilityTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private ApiClient Token(params string[] permissions) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    [Fact]
    public async Task A_reader_with_so_read_and_cost_view_but_not_so_manage_gets_no_threshold_and_no_amount_reached()
    {
        var submitted = await _s.SubmittedWorthAsync(6000m);

        var read = (await Token(Permissions.SoRead, Permissions.CostView).SalesOrders.GetAsync(submitted.Id)).Data<SalesOrderDto>();

        var trigger = Assert.Single(read.ApprovalTriggers);
        Assert.Equal((ApprovalMetric.Amount, (decimal?)null, (decimal?)null), (trigger.Metric, trigger.Threshold, trigger.Actual));
        Assert.Null(read.TotalAmount);
    }

    [Fact]
    public async Task A_reader_with_so_manage_and_cost_view_gets_the_threshold_and_the_amount_reached_and_a_quantity_rule_is_never_hidden()
    {
        var submitted = await _s.SubmittedWorthAsync(6000m);

        var read = (await Token(Permissions.SoRead, Permissions.SoManage, Permissions.CostView).SalesOrders.GetAsync(submitted.Id)).Data<SalesOrderDto>();

        var trigger = Assert.Single(read.ApprovalTriggers);
        Assert.Equal((5000m, 6000m), (trigger.Threshold, trigger.Actual));
        Assert.Equal(6000m, read.TotalAmount);
    }

    private async Task<string> AuditBodyAsync(ApiClient reader, string action, string no) =>
        (await reader.Audit.QueryAsync(entityType: AuditEntities.SalesOrder, entityId: no, action: action)).ShouldBeOk().Body;

    [Fact]
    public async Task The_audit_of_a_created_and_a_submitted_order_shows_the_selling_amount_only_to_a_reader_with_so_manage()
    {
        var submitted = await _s.SubmittedWorthAsync(6000m);
        var withoutManage = Token(Permissions.AuditRead, Permissions.SoRead, Permissions.CostView);
        var withManage = Token(Permissions.AuditRead, Permissions.SoRead, Permissions.SoManage);

        foreach (var action in new[] { AuditActions.SalesOrderCreated, AuditActions.SalesOrderSubmitted })
        {
            var blind = await AuditBodyAsync(withoutManage, action, submitted.No);
            var sighted = await AuditBodyAsync(withManage, action, submitted.No);

            Assert.Contains(action, blind);                               // the event is there
            Assert.DoesNotContain("SellingAmount", blind);                // cost.view does not unlock a selling price
            Assert.DoesNotContain("6000", blind);
            Assert.Contains("SellingAmount", sighted);
            Assert.Contains("6000", sighted);
        }
    }

    [Fact]
    public async Task The_audit_of_a_created_order_shows_no_price_to_a_reader_with_neither_cost_view_nor_so_manage()
    {
        var order = await _s.DraftWorthAsync(1234.5m);

        var body = await AuditBodyAsync(Token(Permissions.AuditRead), AuditActions.SalesOrderCreated, order.No);

        Assert.Contains(AuditActions.SalesOrderCreated, body);
        Assert.DoesNotContain("Amount", body);
        Assert.DoesNotContain("1234", body);
    }
}

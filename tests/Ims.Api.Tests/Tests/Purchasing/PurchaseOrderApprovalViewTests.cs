using System.Net;
using System.Text.Json;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Purchasing;
using Ims.Application.Features.Settings;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// PL2.3 hidden amounts: a caller without <c>cost.view</c> learns no amount from the order, the approval triggers or the response to an approval, and sees that a rule held the order and of which
/// kind; a caller with it sees the threshold and the amount reached. (The role-by-state table, the list, the counts and the Settings rules follow in PL2.3b.)
/// </summary>
[Collection(ApiCollection.Name)]
public class PurchaseOrderApprovalViewTests(ApiFixture fixture) : IAsyncLifetime
{
    private PurchasingScenario _p = null!;

    public async Task InitializeAsync() => _p = await PurchasingScenario.CreateAsync(fixture);

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    private ApiClient Forged(IEnumerable<string> permissions) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    // ----- acceptance 6: allowedActions -----

    // ----- hidden amounts -----
    private static readonly string[] MoneyKeys = ["totalAmount", "unitPrice", "amount", "threshold", "actual"];

    private static int NullMoney(string json)
    {
        var seen = 0;
        void Walk(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject())
                    {
                        if (MoneyKeys.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) { seen++; Assert.Equal(JsonValueKind.Null, p.Value.ValueKind); }
                        Walk(p.Value);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in e.EnumerateArray()) Walk(item);
                    break;
            }
        }
        Walk(JsonDocument.Parse(json).RootElement);
        return seen;
    }

    [Fact]
    public async Task A_caller_without_cost_view_sees_that_a_rule_held_the_order_and_which_kind_but_not_the_amount_it_reached_or_the_threshold()
    {
        var submitted = await _p.SubmittedWorthAsync(6000m);

        var detail = await _p.Staff.PurchaseOrders.GetAsync(submitted.Id);
        var trigger = Assert.Single(detail.Data<PurchaseOrderDto>().ApprovalTriggers);

        Assert.Equal((ApprovalMetric.Amount, (decimal?)null, (decimal?)null, ApproverTier.Standard), (trigger.Metric, trigger.Threshold, trigger.Actual, trigger.ApproverTier));
        Assert.True(NullMoney(detail.Body) >= 5, "the total, the line price and amount, the threshold and the amount reached are all present as null"); // the walk finds them all
        var list = await _p.Staff.PurchaseOrders.ListAsync(status: PurchaseOrderStatus.Submitted, warehouseId: _p.Warehouse);
        Assert.True(NullMoney(list.Body) > 0);
    }

    [Fact]
    public async Task A_reviewer_without_cost_view_can_approve_and_the_answer_still_carries_no_amount()
    {
        var submitted = await _p.SubmittedWorthAsync(6000m);
        var reviewer = Forged(PermissionCatalog.Codes.Except([Permissions.CostView]));

        var response = await reviewer.PurchaseOrders.ApproveAsync(submitted.Id, submitted.ConcurrencyToken);

        Assert.Equal(PurchaseOrderStatus.Approved, response.Data<PurchaseOrderDto>().Status);
        Assert.True(NullMoney(response.Body) >= 4);
        Assert.Equal(6000m, (await _p.ReadAsync(submitted.Id)).TotalAmount); // the amount is on the order for those who may see it
    }

    [Fact]
    public async Task A_caller_with_cost_view_sees_the_threshold_and_the_amount_reached()
    {
        var submitted = await _p.SubmittedWorthAsync(6000m);

        var trigger = Assert.Single((await _p.ReadAsync(submitted.Id, _p.Supervisor)).ApprovalTriggers);

        Assert.Equal((5000m, 6000m), (trigger.Threshold, trigger.Actual));
    }


}

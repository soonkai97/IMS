using System.Net;
using System.Text.Json;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Sales;
using Ims.Application.Features.Settings;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// PL3.3 acceptance 6 and 7 and the visibility rules around approval: what each system role is offered on an order in each state (<c>allowedActions</c>) and which list rows it may act on
/// (<c>canAct</c>); that a caller without <c>so.manage</c> learns no amount from the order, the approval triggers or the response to an approval; that sales orders do not leak into the
/// document counts; and that the sales-order rules in Settings exist only while the module is on.
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesOrderApprovalViewTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _p = null!;

    public async Task InitializeAsync() => _p = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private ApiClient Forged(IEnumerable<string> permissions) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    // ----- acceptance 6: allowedActions -----
    [Fact]
    public async Task What_each_system_role_is_offered_on_an_order_is_the_table_of_the_plan_for_every_state()
    {
        var draft = await _p.DraftWorthAsync(10m); // by the Clerk
        var standard = await _p.SubmittedWorthAsync(6000m);
        var large = await _p.SubmittedWorthAsync(25000m);
        var released = await _p.SubmittedWorthAsync(100m);
        var approvedLarge = await _p.SubmittedWorthAsync(30000m);
        approvedLarge = (await _p.Owner.SalesOrders.ApproveAsync(approvedLarge.Id, approvedLarge.ConcurrencyToken)).Data<SalesOrderDto>();
        string[] none = [];
        var (edit, submit, cancel, recall, approve, reject) = (DocumentActions.Edit, DocumentActions.Submit, DocumentActions.Cancel, DocumentActions.Recall, DocumentActions.Approve, DocumentActions.Reject);
        var table = new (string State, SalesOrderDto Order, string[] Admin, string[] Owner, string[] Supervisor, string[] Clerk, string[] Staff)[]
        {
            // so.manage (Administrator, Owner, Supervisor, Clerk) edits and submits any draft; only its creator cancels it, and whoever holds modules.manage (the Administrator) any draft
            ("draft", draft, [edit, submit, cancel], [edit, submit], [edit, submit], [edit, submit, cancel], none),
            ("submitted standard", standard, [approve, reject], none, [approve, reject], [recall, cancel], none),
            ("submitted large", large, [approve, reject], [approve, reject], none, [recall, cancel], none),
            // an order released at once is standard tier: a Supervisor or the Administrator cancels it; delivering and closing short come with PL3.5 to PL3.7
            ("approved by release", released, [cancel], none, [cancel], none, none),
            ("approved large", approvedLarge, [cancel], [cancel], none, none, none)
        };

        foreach (var (state, order, admin, owner, supervisor, clerk, staff) in table)
            foreach (var (role, who, expected) in new[] { (Roles.Admin, _p.Admin, admin), (Roles.Owner, _p.Owner, owner), (Roles.Supervisor, _p.Supervisor, supervisor), (Roles.Clerk, _p.Clerk, clerk), (Roles.WarehouseStaff, _p.Staff, staff) })
                Assert.True(expected.Order().SequenceEqual((await _p.ReadAsync(order.Id, who)).AllowedActions.Order()), $"{state} / {role}");
    }

    [Fact]
    public async Task An_action_offered_is_accepted_and_an_action_not_offered_is_refused()
    {
        var standard = await _p.SubmittedWorthAsync(6000m);

        var offered = (await _p.ReadAsync(standard.Id, _p.Supervisor)).AllowedActions;
        var notOffered = (await _p.ReadAsync(standard.Id, _p.Owner)).AllowedActions;

        Assert.Contains(DocumentActions.Approve, offered);
        Assert.DoesNotContain(DocumentActions.Approve, notOffered);
        (await _p.Owner.SalesOrders.ApproveAsync(standard.Id, standard.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await _p.Supervisor.SalesOrders.ApproveAsync(standard.Id, standard.ConcurrencyToken)).ShouldBeOk();
    }

    [Fact]
    public async Task A_list_row_can_be_acted_on_only_by_the_reviewer_it_is_routed_to_and_never_by_its_submitter()
    {
        var standard = await _p.SubmittedWorthAsync(6000m);
        var large = await _p.SubmittedWorthAsync(25000m);
        var released = await _p.SubmittedWorthAsync(100m);
        var own = await _p.SubmittedWorthAsync(6000m, _p.Supervisor);

        bool CanAct(PagedResult<SalesOrderListItem> page, SalesOrderDto order) => page.Items.Single(i => i.Id == order.Id).CanAct;
        async Task<PagedResult<SalesOrderListItem>> PageAsync(ApiClient who) => (await who.SalesOrders.ListAsync(warehouseId: _p.Warehouse, pageSize: 200)).Data<PagedResult<SalesOrderListItem>>();
        var (supervisor, owner, admin, clerk, staff) = (await PageAsync(_p.Supervisor), await PageAsync(_p.Owner), await PageAsync(_p.Admin), await PageAsync(_p.Clerk), await PageAsync(_p.Staff));

        Assert.Equal((true, false, false, true), (CanAct(supervisor, standard), CanAct(supervisor, large), CanAct(supervisor, released), CanAct(supervisor, own) == false));
        Assert.Equal((false, true, false), (CanAct(owner, standard), CanAct(owner, large), CanAct(owner, released)));
        Assert.Equal((true, true, false), (CanAct(admin, standard), CanAct(admin, large), CanAct(admin, released)));
        Assert.All(new[] { clerk, staff }, page => Assert.All(new[] { standard, large, released, own }, o => Assert.False(CanAct(page, o))));
        Assert.True(CanAct(admin, own)); // the Administrator did not submit it
        Assert.Equal(SalesOrderStatus.Approved, supervisor.Items.Single(i => i.Id == released.Id).Status);
    }

    [Fact]
    public async Task The_list_filters_the_orders_waiting_for_approval_by_status_and_carries_whether_a_rule_held_them()
    {
        var waiting = await _p.SubmittedWorthAsync(6000m);
        var released = await _p.SubmittedWorthAsync(100m);

        var page = (await _p.Supervisor.SalesOrders.ListAsync(status: SalesOrderStatus.Submitted, warehouseId: _p.Warehouse)).Data<PagedResult<SalesOrderListItem>>();

        Assert.Contains(waiting.Id, page.Items.Select(i => i.Id));
        Assert.DoesNotContain(released.Id, page.Items.Select(i => i.Id));
        Assert.All(page.Items, i => Assert.True(i.RequiresApproval));
    }

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
    public async Task A_caller_without_so_manage_sees_that_a_rule_held_the_order_and_which_kind_but_not_the_amount_it_reached_or_the_threshold()
    {
        var submitted = await _p.SubmittedWorthAsync(6000m);

        var detail = await _p.Staff.SalesOrders.GetAsync(submitted.Id);
        var trigger = Assert.Single(detail.Data<SalesOrderDto>().ApprovalTriggers);

        Assert.Equal((ApprovalMetric.Amount, (decimal?)null, (decimal?)null, ApproverTier.Standard), (trigger.Metric, trigger.Threshold, trigger.Actual, trigger.ApproverTier));
        Assert.Equal(2, NullMoney(detail.Body)); // the threshold and the amount reached are present as null (the rule's numbers need cost.view); the selling price, amount and total are absent altogether
        var list = await _p.Staff.SalesOrders.ListAsync(status: SalesOrderStatus.Submitted, warehouseId: _p.Warehouse);
        Assert.Equal(0, NullMoney(list.Body));
    }

    [Fact]
    public async Task A_reviewer_without_so_manage_can_approve_and_the_answer_still_carries_no_amount()
    {
        var submitted = await _p.SubmittedWorthAsync(6000m);
        var reviewer = Forged(PermissionCatalog.Codes.Except([Permissions.SoManage]));

        var response = await reviewer.SalesOrders.ApproveAsync(submitted.Id, submitted.ConcurrencyToken);

        Assert.Equal(SalesOrderStatus.Approved, response.Data<SalesOrderDto>().Status);
        Assert.DoesNotContain("totalAmount", response.Body);
        Assert.DoesNotContain("unitPrice", response.Body);
        Assert.Equal(6000m, (await _p.ReadAsync(submitted.Id)).TotalAmount); // the amount is on the order for those who may see it
    }

    [Fact]
    public async Task A_caller_with_cost_view_sees_the_threshold_and_the_amount_reached()
    {
        var submitted = await _p.SubmittedWorthAsync(6000m);

        var trigger = Assert.Single((await _p.ReadAsync(submitted.Id, _p.Supervisor)).ApprovalTriggers);

        Assert.Equal((5000m, 6000m), (trigger.Threshold, trigger.Actual));
    }

    // ----- the counts -----
    [Fact]
    public async Task A_sales_order_waiting_for_approval_is_not_in_the_document_counts_of_any_role()
    {
        async Task<int[]> WaitingAsync() => [.. await Task.WhenAll(new[] { _p.Supervisor, _p.Owner, _p.Admin, _p.Clerk, _p.Staff }.Select(async c => (await c.Documents.CountsAsync()).Data<DocumentCounts>().ApprovalsWaiting))];
        var before = await WaitingAsync();

        await _p.SubmittedWorthAsync(6000m);
        await _p.SubmittedWorthAsync(25000m);

        Assert.Equal(before, await WaitingAsync()); // the Home tile and menu badge count documents; the orders have the status filter of their own list
    }

    // ----- acceptance 7: the rules in Settings -----
    private async Task<List<ApprovalRuleDto>> RulesAsync(ApiClient? who = null) => (await (who ?? _p.Admin).Settings.ListRulesAsync()).Data<List<ApprovalRuleDto>>();

    [Fact]
    public async Task The_two_placeholder_rules_of_the_scenario_are_listed_while_the_module_is_on()
    {
        var rules = (await RulesAsync()).Where(r => r.DocType == DocumentType.SalesOrder && r.IsActive).OrderBy(r => r.AmountThreshold).ToList(); // the inactive ones are what other tests added and switched off

        Assert.Equal([(5000m, ApproverTier.Standard), (20000m, ApproverTier.Large)], rules.Select(r => (r.AmountThreshold!.Value, r.ApproverTier)));
        Assert.All(rules, r => Assert.True(r.IsActive));
        Assert.Contains(await RulesAsync(), r => r.DocType == DocumentType.Issue); // the document rules are untouched
    }

    [Fact]
    public async Task While_the_module_is_off_a_sales_order_rule_cannot_be_written_or_found_and_is_not_listed_but_is_kept()
    {
        var kept = (await RulesAsync()).Single(r => r.DocType == DocumentType.SalesOrder && r.ApproverTier == ApproverTier.Large);
        await _p.TurnOffAsync();
        var rule = new ApprovalRuleSaveRequest(DocumentType.SalesOrder, 1000m, null, true, ApproverTier.Standard);

        var create = (await _p.Admin.Settings.CreateRuleAsync(rule)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _p.Admin.Settings.UpdateRuleAsync(kept.Id, new(DocumentType.SalesOrder, 1m, null, true, ApproverTier.Standard))).ShouldBeProblem(HttpStatusCode.NotFound);
        (await _p.Admin.Settings.UpdateRuleAsync(kept.Id, new(DocumentType.Receipt, 1m, null, true, ApproverTier.Standard))).ShouldBeProblem(HttpStatusCode.NotFound); // nor moved to another subject

        Assert.Contains("docType", create.Detail);
        Assert.DoesNotContain(await RulesAsync(), r => r.DocType == DocumentType.SalesOrder);
        Assert.Contains(await RulesAsync(), r => r.DocType == DocumentType.Issue);
        Assert.Equal(20000m, await fixture.Database.ScalarAsync<decimal>("SELECT AmountThreshold FROM ApprovalRule WHERE PublicId = @id", ("@id", kept.Id))); // untouched

        await fixture.Database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (N'Module.sales', N'true')");
        Assert.Contains(await RulesAsync(), r => r.Id == kept.Id && r.AmountThreshold == 20000m && r.IsActive);
    }

    [Fact]
    public async Task While_the_module_is_on_a_sales_order_rule_is_written_and_changes_the_routing_of_the_next_submit()
    {
        await using var rule = await ApprovalRuleScope.AddAsync(_p.Admin, DocumentType.SalesOrder, 1000m, null, Roles.Supervisor);

        var order = await _p.SubmittedWorthAsync(1500m);

        Assert.Equal((SalesOrderStatus.Submitted, false), (order.Status, order.IsLarge));
    }
}

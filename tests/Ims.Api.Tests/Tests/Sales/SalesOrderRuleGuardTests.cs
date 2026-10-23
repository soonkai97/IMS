using System.Net;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Sales;
using Ims.Application.Features.Settings;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// PL3.3: sales must not run without an approval rule (with none every priced order is released with no approver). It cannot be switched on while no sales-order rule is active, the last
/// active one cannot be switched off while it is on, a rule cannot move to or from sales orders. Also: an Administrator cancels any
/// Draft (so a departed creator's Draft cannot block the switch-off), and cancelling needs so.read even for a token that may cancel.
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesOrderRuleGuardTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _p = null!;

    public async Task InitializeAsync() => _p = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private static string Why() => $"Rule guard test {Guid.NewGuid():N}"[..28];

    private async Task<List<ApprovalRuleDto>> SalesRulesAsync(bool? active = null) =>
        (await _p.Admin.Settings.ListRulesAsync()).Data<List<ApprovalRuleDto>>().Where(r => r.DocType == DocumentType.SalesOrder && (active is null || r.IsActive == active)).ToList();

    private Task SetActiveAsync(IEnumerable<Guid> ids, int value) =>
        Task.WhenAll(ids.Select(id => fixture.Database.ExecuteAsync("UPDATE ApprovalRule SET IsActive = @v WHERE PublicId = @id", ("@v", value), ("@id", id))));

    // ----- turning on -----
    [Fact]
    public async Task Sales_is_not_turned_on_while_no_sales_order_rule_is_active_and_is_once_one_is()
    {
        var active = (await SalesRulesAsync(active: true)).Select(r => r.Id).ToList();
        await _p.TurnOffAsync();
        await SetActiveAsync(active, 0);
        try
        {
            var audit = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE [Action] LIKE N'module.%'");

            var refused = (await _p.Admin.Modules.SwitchAsync(ModuleCodes.Sales, true, Why())).ShouldBeProblem(HttpStatusCode.Conflict);

            Assert.Equal(Messages.SalesOrderRuleNeededToTurnOn, refused.Detail);
            Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SystemSetting WHERE [Key] = N'Module.sales'"));
            Assert.Equal(audit, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE [Action] LIKE N'module.%'"));
            await SetActiveAsync(active.Take(1), 1);
            (await _p.Admin.Modules.SwitchAsync(ModuleCodes.Sales, true, Why())).ShouldBeOk();
        }
        finally
        {
            await SetActiveAsync(active, 1);
        }
    }

    [Fact]
    public async Task Turning_sales_off_and_on_again_with_the_rules_in_place_is_not_in_the_way()
    {
        (await _p.Admin.Modules.SwitchAsync(ModuleCodes.Sales, false, Why())).ShouldBeOk();

        (await _p.Admin.Modules.SwitchAsync(ModuleCodes.Sales, true, Why())).ShouldBeOk();
    }

    // ----- the last rule, and the type of a rule -----
    [Fact]
    public async Task While_sales_is_on_all_but_the_last_active_rule_can_be_switched_off_and_the_last_one_is_409()
    {
        var rules = await SalesRulesAsync(active: true);
        Assert.True(rules.Count >= 2, "the test scenario writes two");
        ApprovalRuleSaveRequest Off(ApprovalRuleDto r) => new(r.DocType, r.AmountThreshold, r.QtyThreshold, false, r.ApproverTier);
        try
        {
            foreach (var r in rules.Take(rules.Count - 1)) (await _p.Admin.Settings.UpdateRuleAsync(r.Id, Off(r))).ShouldBeOk();

            var last = rules[^1];
            var refused = (await _p.Admin.Settings.UpdateRuleAsync(last.Id, Off(last))).ShouldBeProblem(HttpStatusCode.Conflict);

            Assert.Equal(Messages.SalesOrderLastRule, refused.Detail);
            Assert.Single(await SalesRulesAsync(active: true));
            // an edit that keeps it active is fine
            (await _p.Admin.Settings.UpdateRuleAsync(last.Id, new(last.DocType, last.AmountThreshold, last.QtyThreshold, true, last.ApproverTier))).ShouldBeOk();
        }
        finally
        {
            await SetActiveAsync(rules.Select(r => r.Id), 1);
        }
    }

    [Fact]
    public async Task A_rule_cannot_be_moved_onto_or_off_sales_orders_400()
    {
        var order = (await SalesRulesAsync()).First();
        var issue = (await _p.Admin.Settings.ListRulesAsync()).Data<List<ApprovalRuleDto>>().First(r => r.DocType == DocumentType.Issue);

        var onto = (await _p.Admin.Settings.UpdateRuleAsync(issue.Id, new(DocumentType.SalesOrder, issue.AmountThreshold, issue.QtyThreshold, issue.IsActive, issue.ApproverTier))).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _p.Admin.Settings.UpdateRuleAsync(order.Id, new(DocumentType.Issue, order.AmountThreshold, order.QtyThreshold, order.IsActive, order.ApproverTier))).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(Messages.SalesOrderRuleTypeFixed, onto.Detail);
        Assert.Equal(DocumentType.Issue, (await _p.Admin.Settings.ListRulesAsync()).Data<List<ApprovalRuleDto>>().Single(r => r.Id == issue.Id).DocType);
        Assert.Contains(await SalesRulesAsync(), r => r.Id == order.Id);
    }

    [Fact]
    public async Task Two_administrators_switching_off_the_two_active_rules_at_once_leave_one_active()
    {
        var rules = await SalesRulesAsync(active: true);
        var surplus = rules.Skip(2).Select(r => r.Id).ToList(); // other tests' rules, still active: the race needs exactly two
        var pair = rules.Take(2).ToList();
        ApprovalRuleSaveRequest Off(ApprovalRuleDto r) => new(r.DocType, r.AmountThreshold, r.QtyThreshold, false, r.ApproverTier);
        await SetActiveAsync(surplus, 0);
        try
        {
            for (var round = 0; round < 10; round++)
            {
                await SetActiveAsync(pair.Select(r => r.Id), 1);

                var results = await Concurrently.RunAsync<ApiResponse>(pair.Select<ApprovalRuleDto, Func<Task<ApiResponse>>>(r => () => _p.Admin.Settings.UpdateRuleAsync(r.Id, Off(r))));

                Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], results.Select(r => r.Status).Order());
                Assert.Single(await SalesRulesAsync(active: true));
            }
        }
        finally
        {
            await SetActiveAsync(rules.Select(r => r.Id), 1);
        }
    }

    // ----- cancel -----
    [Fact]
    public async Task An_administrator_cancels_any_draft_a_supervisor_only_their_own_and_that_clears_the_way_for_the_switch_off()
    {
        var draft = await _p.DraftWorthAsync(10m); // by the Clerk

        (await _p.Supervisor.SalesOrders.CancelAsync(draft.Id, null, draft.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);
        Assert.Contains(DocumentActions.Cancel, (await _p.ReadAsync(draft.Id)).AllowedActions); // the Administrator is offered it
        var cancelled = (await _p.Admin.SalesOrders.CancelAsync(draft.Id, null, draft.ConcurrencyToken)).Data<SalesOrderDto>();

        Assert.Equal(SalesOrderStatus.Cancelled, cancelled.Status);
        (await _p.Admin.Modules.SwitchAsync(ModuleCodes.Sales, false, Why())).ShouldBeOk();
    }

    [Fact]
    public async Task Cancelling_needs_so_read_even_for_a_token_that_may_cancel_the_order()
    {
        var approved = await _p.SubmittedWorthAsync(100m); // released at once: standard tier, so any holder of the standard tier may cancel it
        ApiClient Token(params string[] except) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: PermissionCatalog.Codes.Except(except)));

        (await Token(Permissions.SoRead).SalesOrders.CancelAsync(approved.Id, "no longer needed", approved.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Equal(SalesOrderStatus.Approved, (await _p.ReadAsync(approved.Id)).Status); // the same token with so.read would have been let through:
        Assert.Equal(SalesOrderStatus.Cancelled, (await Token().SalesOrders.CancelAsync(approved.Id, "no longer needed", approved.ConcurrencyToken)).Data<SalesOrderDto>().Status);
    }

    [Fact]
    public async Task The_door_of_cancel_is_open_to_modules_manage_alone_so_a_departed_creators_draft_cannot_block_the_switch_off_but_only_a_draft()
    {
        var draft = await _p.DraftWorthAsync(10m);
        var submitted = await _p.SubmittedWorthAsync(6000m);
        var token = fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: [Permissions.ModulesManage, Permissions.SoRead]));

        (await token.SalesOrders.CancelAsync(submitted.Id, null, submitted.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden); // a Submitted order stays its submitter's
        var cancelled = (await token.SalesOrders.CancelAsync(draft.Id, null, draft.ConcurrencyToken)).Data<SalesOrderDto>();

        Assert.Equal(SalesOrderStatus.Cancelled, cancelled.Status);
    }
}

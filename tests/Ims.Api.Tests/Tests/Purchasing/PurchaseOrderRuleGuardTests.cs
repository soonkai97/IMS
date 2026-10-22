using System.Net;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Purchasing;
using Ims.Application.Features.Settings;
using Ims.Domain;
using Ims.Infrastructure.Persistence;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// PL2.3b: purchasing must not run without an approval rule (with none every priced order is released with no approver). It cannot be switched on while no purchase-order rule is active, the last
/// active one cannot be switched off while it is on, a rule cannot move to or from purchase orders, and the Seeder that writes the standard pair is idempotent. Also: an Administrator cancels any
/// Draft (so a departed creator's Draft cannot block the switch-off), and cancelling needs po.read even for a token that may cancel.
/// </summary>
[Collection(ApiCollection.Name)]
public class PurchaseOrderRuleGuardTests(ApiFixture fixture) : IAsyncLifetime
{
    private PurchasingScenario _p = null!;

    public async Task InitializeAsync() => _p = await PurchasingScenario.CreateAsync(fixture);

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    private static string Why() => $"Rule guard test {Guid.NewGuid():N}"[..28];

    private async Task<List<ApprovalRuleDto>> PurchaseRulesAsync(bool? active = null) =>
        (await _p.Admin.Settings.ListRulesAsync()).Data<List<ApprovalRuleDto>>().Where(r => r.DocType == DocumentType.PurchaseOrder && (active is null || r.IsActive == active)).ToList();

    private Task SetActiveAsync(IEnumerable<Guid> ids, int value) =>
        Task.WhenAll(ids.Select(id => fixture.Database.ExecuteAsync("UPDATE ApprovalRule SET IsActive = @v WHERE PublicId = @id", ("@v", value), ("@id", id))));

    // ----- turning on -----
    [Fact]
    public async Task Purchasing_is_not_turned_on_while_no_purchase_order_rule_is_active_and_is_once_one_is()
    {
        var active = (await PurchaseRulesAsync(active: true)).Select(r => r.Id).ToList();
        await _p.TurnOffAsync();
        await SetActiveAsync(active, 0);
        try
        {
            var audit = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE [Action] LIKE N'module.%'");

            var refused = (await _p.Admin.Modules.SwitchAsync(ModuleCodes.Purchasing, true, Why())).ShouldBeProblem(HttpStatusCode.Conflict);

            Assert.Equal(Messages.PurchaseOrderRuleNeededToTurnOn, refused.Detail);
            Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SystemSetting WHERE [Key] = N'Module.purchasing'"));
            Assert.Equal(audit, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE [Action] LIKE N'module.%'"));
            await SetActiveAsync(active.Take(1), 1);
            (await _p.Admin.Modules.SwitchAsync(ModuleCodes.Purchasing, true, Why())).ShouldBeOk();
        }
        finally
        {
            await SetActiveAsync(active, 1);
        }
    }

    [Fact]
    public async Task Turning_purchasing_off_and_on_again_with_the_rules_in_place_is_not_in_the_way()
    {
        (await _p.Admin.Modules.SwitchAsync(ModuleCodes.Purchasing, false, Why())).ShouldBeOk();

        (await _p.Admin.Modules.SwitchAsync(ModuleCodes.Purchasing, true, Why())).ShouldBeOk();
    }

    // ----- the last rule, and the type of a rule -----
    [Fact]
    public async Task While_purchasing_is_on_all_but_the_last_active_rule_can_be_switched_off_and_the_last_one_is_409()
    {
        var rules = await PurchaseRulesAsync(active: true);
        Assert.True(rules.Count >= 2, "the Seeder writes two");
        ApprovalRuleSaveRequest Off(ApprovalRuleDto r) => new(r.DocType, r.AmountThreshold, r.QtyThreshold, false, r.ApproverTier);
        try
        {
            foreach (var r in rules.Take(rules.Count - 1)) (await _p.Admin.Settings.UpdateRuleAsync(r.Id, Off(r))).ShouldBeOk();

            var last = rules[^1];
            var refused = (await _p.Admin.Settings.UpdateRuleAsync(last.Id, Off(last))).ShouldBeProblem(HttpStatusCode.Conflict);

            Assert.Equal(Messages.PurchaseOrderLastRule, refused.Detail);
            Assert.Single(await PurchaseRulesAsync(active: true));
            // an edit that keeps it active is fine
            (await _p.Admin.Settings.UpdateRuleAsync(last.Id, new(last.DocType, last.AmountThreshold, last.QtyThreshold, true, last.ApproverTier))).ShouldBeOk();
        }
        finally
        {
            await SetActiveAsync(rules.Select(r => r.Id), 1);
        }
    }

    [Fact]
    public async Task A_rule_cannot_be_moved_onto_or_off_purchase_orders_400()
    {
        var purchase = (await PurchaseRulesAsync()).First();
        var issue = (await _p.Admin.Settings.ListRulesAsync()).Data<List<ApprovalRuleDto>>().First(r => r.DocType == DocumentType.Issue);

        var onto = (await _p.Admin.Settings.UpdateRuleAsync(issue.Id, new(DocumentType.PurchaseOrder, issue.AmountThreshold, issue.QtyThreshold, issue.IsActive, issue.ApproverTier))).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _p.Admin.Settings.UpdateRuleAsync(purchase.Id, new(DocumentType.Issue, purchase.AmountThreshold, purchase.QtyThreshold, purchase.IsActive, purchase.ApproverTier))).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(Messages.PurchaseOrderRuleTypeFixed, onto.Detail);
        Assert.Equal(DocumentType.Issue, (await _p.Admin.Settings.ListRulesAsync()).Data<List<ApprovalRuleDto>>().Single(r => r.Id == issue.Id).DocType);
        Assert.Contains(await PurchaseRulesAsync(), r => r.Id == purchase.Id);
    }

    [Fact]
    public async Task Two_administrators_switching_off_the_two_active_rules_at_once_leave_one_active()
    {
        var rules = await PurchaseRulesAsync(active: true);
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
                Assert.Single(await PurchaseRulesAsync(active: true));
            }
        }
        finally
        {
            await SetActiveAsync(rules.Select(r => r.Id), 1);
        }
    }

    // ----- the Seeder -----
    [Fact]
    public async Task The_seeder_writes_the_pair_once_and_neither_adds_nor_revives_rules_when_it_runs_again()
    {
        var before = await PurchaseRulesAsync();
        var victim = before.First();
        await SetActiveAsync([victim.Id], 0);
        try
        {
            await Seeder.SeedAsync(fixture.Host.Services, development: true);
            await Seeder.SeedAsync(fixture.Host.Services, development: true);

            var after = await PurchaseRulesAsync();
            Assert.Equal(before.Select(r => r.Id).Order(), after.Select(r => r.Id).Order()); // no new row
            Assert.False(after.Single(r => r.Id == victim.Id).IsActive); // and a rule an Administrator switched off stays off
        }
        finally
        {
            await SetActiveAsync([victim.Id], 1);
        }
    }

    // ----- cancel -----
    [Fact]
    public async Task An_administrator_cancels_any_draft_a_supervisor_only_their_own_and_that_clears_the_way_for_the_switch_off()
    {
        var draft = await _p.DraftWorthAsync(10m); // by the Clerk

        (await _p.Supervisor.PurchaseOrders.CancelAsync(draft.Id, null, draft.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);
        Assert.Contains(DocumentActions.Cancel, (await _p.ReadAsync(draft.Id)).AllowedActions); // the Administrator is offered it
        var cancelled = (await _p.Admin.PurchaseOrders.CancelAsync(draft.Id, null, draft.ConcurrencyToken)).Data<PurchaseOrderDto>();

        Assert.Equal(PurchaseOrderStatus.Cancelled, cancelled.Status);
        (await _p.Admin.Modules.SwitchAsync(ModuleCodes.Purchasing, false, Why())).ShouldBeOk();
    }

    [Fact]
    public async Task Cancelling_needs_po_read_even_for_a_token_that_may_cancel_the_order()
    {
        var approved = await _p.SubmittedWorthAsync(100m); // released at once: standard tier, so any holder of the standard tier may cancel it
        ApiClient Token(params string[] except) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: PermissionCatalog.Codes.Except(except)));

        (await Token(Permissions.PoRead).PurchaseOrders.CancelAsync(approved.Id, "no longer needed", approved.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Equal(PurchaseOrderStatus.Approved, (await _p.ReadAsync(approved.Id)).Status); // the same token with po.read would have been let through:
        Assert.Equal(PurchaseOrderStatus.Cancelled, (await Token().PurchaseOrders.CancelAsync(approved.Id, "no longer needed", approved.ConcurrencyToken)).Data<PurchaseOrderDto>().Status);
    }

    [Fact]
    public async Task The_door_of_cancel_stays_shut_for_modules_manage_alone_the_draft_rule_needs_a_caller_who_gets_through_it()
    {
        var draft = await _p.DraftWorthAsync(10m);
        var token = fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: [Permissions.ModulesManage, Permissions.PoRead]));

        (await token.PurchaseOrders.CancelAsync(draft.Id, null, draft.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden); // the policy asks for po.manage or an approval tier

        Assert.Equal(PurchaseOrderStatus.Draft, (await _p.ReadAsync(draft.Id)).Status);
    }
}

using System.Net;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Sales;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// Two writers at the same moment on one order (PL3.3 review): the version token lets exactly one through, the other is a 409, the audit trail holds the winner's row only, and what is stored
/// is consistent (the tier an order was routed to matches its total). Each race runs several rounds on a fresh order, because a race is a matter of timing.
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesOrderConcurrencyTests(ApiFixture fixture) : IAsyncLifetime
{
    private const int Rounds = 10;
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private Task<int> AuditCountAsync(string action, string no) =>
        fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityType = @t AND EntityId = @no", ("@a", action), ("@t", AuditEntities.SalesOrder), ("@no", no));

    private async Task<int> AuditTotalAsync(string no) =>
        await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE EntityType = @t AND EntityId = @no AND Action LIKE N'sales-order.%'", ("@t", AuditEntities.SalesOrder), ("@no", no));

    private static void ExactlyOneWon(IReadOnlyList<ApiResponse> results)
    {
        Assert.Equal(1, results.Count(r => r.Status == HttpStatusCode.OK));
        Assert.All(results.Where(r => r.Status != HttpStatusCode.OK), r => r.ShouldBeProblem(HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task A_recall_racing_an_approval_lets_exactly_one_through_and_the_audit_holds_only_the_winner()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var order = await _s.SubmittedWorthAsync(6000m);

            var results = await Concurrently.RunAsync<ApiResponse>([() => _s.Clerk.SalesOrders.RecallAsync(order.Id, order.ConcurrencyToken), () => _s.Supervisor.SalesOrders.ApproveAsync(order.Id, order.ConcurrencyToken)]);

            ExactlyOneWon(results);
            var after = await _s.ReadAsync(order.Id);
            var recallWon = results[0].Status == HttpStatusCode.OK;
            Assert.Equal(recallWon ? SalesOrderStatus.Draft : SalesOrderStatus.Approved, after.Status);
            Assert.Equal((recallWon ? 1 : 0, recallWon ? 0 : 1), (await AuditCountAsync(AuditActions.SalesOrderRecalled, order.No), await AuditCountAsync(AuditActions.SalesOrderApproved, order.No)));
        }
    }

    [Fact]
    public async Task A_reject_racing_an_approval_lets_exactly_one_through_and_the_audit_holds_only_the_winner()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var order = await _s.SubmittedWorthAsync(6000m);

            var results = await Concurrently.RunAsync<ApiResponse>([() => _s.Admin.SalesOrders.RejectAsync(order.Id, "not this one", order.ConcurrencyToken), () => _s.Supervisor.SalesOrders.ApproveAsync(order.Id, order.ConcurrencyToken)]);

            ExactlyOneWon(results);
            var rejectWon = results[0].Status == HttpStatusCode.OK;
            Assert.Equal(rejectWon ? SalesOrderStatus.Draft : SalesOrderStatus.Approved, (await _s.ReadAsync(order.Id)).Status);
            Assert.Equal((rejectWon ? 1 : 0, rejectWon ? 0 : 1), (await AuditCountAsync(AuditActions.SalesOrderRejected, order.No), await AuditCountAsync(AuditActions.SalesOrderApproved, order.No)));
        }
    }

    [Fact]
    public async Task Two_submits_of_the_same_draft_give_one_200_one_409_and_one_audit_row()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var draft = await _s.DraftWorthAsync(6000m);

            var results = await Concurrently.RunAsync<ApiResponse>([() => _s.Clerk.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken), () => _s.Clerk.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)]);

            ExactlyOneWon(results);
            Assert.Equal(1, await AuditCountAsync(AuditActions.SalesOrderSubmitted, draft.No));
        }
    }

    [Fact]
    public async Task Two_cancels_of_the_same_draft_give_one_200_one_409_and_one_audit_row()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var draft = await _s.DraftWorthAsync(10m);

            var results = await Concurrently.RunAsync<ApiResponse>([() => _s.Clerk.SalesOrders.CancelAsync(draft.Id, null, draft.ConcurrencyToken), () => _s.Clerk.SalesOrders.CancelAsync(draft.Id, null, draft.ConcurrencyToken)]);

            ExactlyOneWon(results);
            Assert.Equal(1, await AuditCountAsync(AuditActions.SalesOrderCancelled, draft.No));
        }
    }

    [Fact]
    public async Task A_save_racing_a_submit_never_leaves_a_tier_that_does_not_match_the_stored_total()
    {
        var product = await _s.NewProductAsync();
        for (var round = 0; round < Rounds; round++)
        {
            var draft = await _s.DraftWorthAsync(6000m);
            var bigger = new Builders.SalesOrderBuilder(_s.Warehouse, fixture.Reference.CustomerId).WithLine(product.Id, 1m, 25000m).Build();

            var results = await Concurrently.RunAsync<ApiResponse>([() => _s.Clerk.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken), () => _s.Clerk.SalesOrders.UpdateAsync(draft.Id, bigger, draft.ConcurrencyToken)]);

            ExactlyOneWon(results);
            var stored = await _s.ReadAsync(draft.Id);
            if (results[0].Status == HttpStatusCode.OK)
            {
                Assert.Equal((SalesOrderStatus.Submitted, 6000m, false), (stored.Status, stored.TotalAmount, stored.IsLarge)); // routed by the total it was submitted with
            }
            else
            {
                Assert.Equal((SalesOrderStatus.Draft, 25000m, false), (stored.Status, stored.TotalAmount, stored.IsLarge)); // the save won: nothing was routed yet
            }
        }
    }

    // ----- a rule changed after the order was routed -----
    [Fact]
    public async Task A_rule_changed_between_submit_and_approve_does_not_change_the_stored_tier_and_a_large_order_is_not_downgraded()
    {
        var large = await _s.SubmittedWorthAsync(25000m);
        Assert.True(large.IsLarge);
        // The Large rule is raised out of this order's reach, so the rules in force now would call it a standard order.
        await fixture.Database.ExecuteAsync("UPDATE ApprovalRule SET AmountThreshold = 900000 WHERE DocType = 6 AND Tier = 2");

        (await _s.Supervisor.SalesOrders.ApproveAsync(large.Id, large.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden); // still a Large order: a Supervisor may not approve it
        var approved = (await _s.Owner.SalesOrders.ApproveAsync(large.Id, large.ConcurrencyToken)).Data<SalesOrderDto>();

        Assert.Equal((SalesOrderStatus.Approved, true), (approved.Status, approved.IsLarge));
    }

    [Fact]
    public async Task A_standard_order_keeps_its_stored_tier_when_every_rule_is_switched_off_after_it_was_submitted()
    {
        var standard = await _s.SubmittedWorthAsync(6000m);
        await fixture.Database.ExecuteAsync("UPDATE ApprovalRule SET IsActive = 0 WHERE DocType = 6");

        var approved = (await _s.Supervisor.SalesOrders.ApproveAsync(standard.Id, standard.ConcurrencyToken)).Data<SalesOrderDto>();

        Assert.Equal((SalesOrderStatus.Approved, false, true), (approved.Status, approved.IsLarge, approved.RequiresApproval));
    }

    // ----- enabling sales against the switch-off of the last rule -----
    [Fact]
    public async Task Switching_sales_on_racing_the_switch_off_of_the_last_active_rule_never_ends_with_sales_on_and_no_active_rule()
    {
        for (var round = 0; round < Rounds; round++)
        {
            await SalesScenario.ResetAsync(fixture); // the module off, the migrated pair back
            await fixture.Database.ExecuteAsync("UPDATE ApprovalRule SET IsActive = 0 WHERE DocType = 6 AND Tier = 2"); // exactly one active rule is left
            var last = (await _s.Admin.Settings.ListRulesAsync()).Data<List<Application.Features.Settings.ApprovalRuleDto>>().FirstOrDefault(r => r.DocType == DocumentType.SalesOrder && r.IsActive);
            var why = $"Enable race {Guid.NewGuid():N}"[..28];
            // The rule is listed only while the module is on, so when the module is still off the rule is found by SQL and addressed by its public id.
            var lastId = last?.Id ?? await fixture.Database.ScalarAsync<Guid>("SELECT TOP (1) PublicId FROM ApprovalRule WHERE DocType = 6 AND IsActive = 1");
            var off = new Application.Features.Settings.ApprovalRuleSaveRequest(DocumentType.SalesOrder, 5000m, null, false, ApproverTier.Standard);

            var results = await Concurrently.RunAsync<ApiResponse>([() => _s.Admin.Modules.SwitchAsync(ModuleCodes.Sales, true, why), () => _s.Admin.Settings.UpdateRuleAsync(lastId, off)]);

            Assert.All(results.Select(r => (int)r.Status), c => Assert.Contains(c, new[] { 200, 404, 409, 503 }));
            var on = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SystemSetting WHERE [Key] = N'Module.sales' AND [Value] = N'true'") == 1;
            var active = await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM ApprovalRule WHERE DocType = 6 AND IsActive = 1 AND REC_DELETED = 0");
            Assert.False(on && active == 0, $"round {round}: sales is on with no active sales-order rule");
        }
    }

    // ----- a refused transition writes nothing -----
    private async Task<byte[]> RowVersionAsync(Guid id) => await fixture.Database.ScalarAsync<byte[]>("SELECT ROW_VERSION FROM SalesOrder WHERE PublicId = @id", ("@id", id));

    [Theory]
    [InlineData("submit")]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("recall")]
    [InlineData("cancel")]
    public async Task A_transition_the_state_does_not_allow_writes_no_audit_row_and_leaves_the_row_version_alone(string action)
    {
        // Each action is tried where the state machine refuses it: submit on an Approved order, approve and reject on a Draft, recall on an Approved order, cancel on a Cancelled one.
        var order = action switch
        {
            "submit" or "recall" => await _s.SubmittedWorthAsync(100m),   // released at once: Approved
            "cancel" => await _s.DraftWorthAsync(10m),
            _ => await _s.DraftWorthAsync(10m)
        };
        if (action == "cancel") order = (await _s.Clerk.SalesOrders.CancelAsync(order.Id, null, order.ConcurrencyToken)).Data<SalesOrderDto>();
        var token = (await _s.ReadAsync(order.Id)).ConcurrencyToken;
        var versionBefore = await RowVersionAsync(order.Id);
        var auditBefore = await AuditTotalAsync(order.No);

        var response = action switch
        {
            "submit" => await _s.Clerk.SalesOrders.SubmitAsync(order.Id, token),
            "approve" => await _s.Supervisor.SalesOrders.ApproveAsync(order.Id, token),
            "reject" => await _s.Supervisor.SalesOrders.RejectAsync(order.Id, "not wanted", token),
            "recall" => await _s.Clerk.SalesOrders.RecallAsync(order.Id, token),
            _ => await _s.Admin.SalesOrders.CancelAsync(order.Id, "again", token)
        };

        Assert.False(response.IsSuccess, $"{action} must be refused");
        Assert.Equal(versionBefore, await RowVersionAsync(order.Id));
        Assert.Equal(auditBefore, await AuditTotalAsync(order.No));
    }
}

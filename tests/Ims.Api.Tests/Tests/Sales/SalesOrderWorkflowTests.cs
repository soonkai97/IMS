using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Sales;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// PL3.3 acceptance 1 to 5 and 9 on a real SQL Server: submit (released at once when no rule fires, otherwise held for the tier the amount reaches), approve, reject, recall and cancel, each with
/// the state it needs, the person it is routed to, a version token, an audit row and nothing written when it is refused. The rules in play are the two placeholders the scenario writes
/// (5,000.00 for a Supervisor, 20,000.00 for the Owner). Clerk makes and submits, Supervisor approves a standard order, Owner a large one, Administrator either.
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesOrderWorkflowTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _p = null!;

    public async Task InitializeAsync() => _p = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private Task<int> AuditCountAsync(string action, string no) =>
        fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityType = @t AND EntityId = @no", ("@a", action), ("@t", AuditEntities.SalesOrder), ("@no", no));

    private static System.Text.Json.JsonElement Json(string text) => System.Text.Json.JsonDocument.Parse(text).RootElement;

    // ----- acceptance 1: submit -----
    [Fact]
    public async Task An_order_below_the_lowest_rule_is_approved_at_once_with_no_approver_and_one_audit_row()
    {
        var draft = await _p.DraftWorthAsync(4999.99m);

        var response = await _p.Clerk.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken);
        var order = response.Data<SalesOrderDto>();

        Assert.Equal((SalesOrderStatus.Approved, false, false), (order.Status, order.RequiresApproval, order.IsLarge));
        Assert.Equal((TestUsers.UsernameOf(Roles.Clerk), (string?)null, true), (order.SubmittedBy, order.ApprovedBy, order.ApprovedAt is not null));
        Assert.Empty(order.ApprovalTriggers);
        Assert.Equal($"\"{order.ConcurrencyToken}\"", response.Header("ETag"));
        Assert.NotEqual(draft.ConcurrencyToken, order.ConcurrencyToken);
        Assert.Equal(1, await AuditCountAsync(AuditActions.SalesOrderSubmitted, order.No));
        var after = Json(await fixture.Database.ScalarAsync<string>("SELECT [After] FROM AuditLogs WHERE EntityId = @no AND Action = @a", ("@no", order.No), ("@a", AuditActions.SalesOrderSubmitted)));
        Assert.Equal(4999.99m, after.GetProperty(AuditFields.Amount).GetDecimal());
    }

    [Fact]
    public async Task An_order_that_reaches_the_standard_rule_waits_for_a_supervisor_and_says_which_threshold_it_reached()
    {
        var order = await _p.SubmittedWorthAsync(5000m);

        Assert.Equal((SalesOrderStatus.Submitted, true, false, null), (order.Status, order.RequiresApproval, order.IsLarge, order.ApprovedBy));
        var trigger = Assert.Single((await _p.ReadAsync(order.Id)).ApprovalTriggers);
        Assert.Equal((ApprovalMetric.Amount, 5000m, 5000m, ApproverTier.Standard), (trigger.Metric, trigger.Threshold, trigger.Actual, trigger.ApproverTier));
    }

    [Fact]
    public async Task An_order_that_reaches_the_large_rule_waits_for_the_owner_and_both_rules_firing_makes_it_large()
    {
        var order = await _p.SubmittedWorthAsync(20000m);

        Assert.Equal((SalesOrderStatus.Submitted, true, true), (order.Status, order.RequiresApproval, order.IsLarge));
        var tiers = (await _p.ReadAsync(order.Id)).ApprovalTriggers.Select(t => t.ApproverTier).Order().ToList();
        Assert.Equal([ApproverTier.Standard, ApproverTier.Large], tiers); // both thresholds are reached; the larger tier decides
    }

    [Fact]
    public async Task A_quantity_rule_for_sales_orders_is_tested_against_the_total_base_quantity()
    {
        await using var rule = await ApprovalRuleScope.AddAsync(_p.Admin, DocumentType.SalesOrder, null, 100m, Roles.Supervisor);
        var product = await _p.NewProductAsync();
        var draft = (await _p.Clerk.SalesOrders.CreateAsync(new SalesOrderBuilder(_p.Warehouse, fixture.Reference.CustomerId).WithLine(product.Id, 100m, 1m).Build())).Data<SalesOrderDto>();

        var order = (await _p.Clerk.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<SalesOrderDto>();

        Assert.Equal(SalesOrderStatus.Submitted, order.Status);
        Assert.Contains(ApprovalMetric.Quantity, (await _p.ReadAsync(order.Id)).ApprovalTriggers.Select(t => t.Metric));
    }

    [Fact]
    public async Task With_no_active_sales_order_rule_every_order_is_released_on_submit()
    {
        // The API refuses to switch off the last active rule while sales is on, so the table is changed directly: the state of a database the Seeder never ran against.
        var active = (await _p.Admin.Settings.ListRulesAsync()).Data<List<Application.Features.Settings.ApprovalRuleDto>>().Where(r => r.DocType == DocumentType.SalesOrder && r.IsActive).Select(r => r.Id).ToList();
        async Task SetAsync(int value) { foreach (var id in active) await fixture.Database.ExecuteAsync("UPDATE ApprovalRule SET IsActive = @v WHERE PublicId = @id", ("@v", value), ("@id", id)); }
        await SetAsync(0);
        try
        {
            Assert.Equal(SalesOrderStatus.Approved, (await _p.SubmittedWorthAsync(999_999m)).Status);
        }
        finally
        {
            await SetAsync(1);
        }
    }

    // ----- acceptance 3: a price above 0 -----
    [Fact]
    public async Task A_draft_with_a_price_of_zero_saves_but_will_not_submit_and_the_refusal_names_the_line()
    {
        var product = await _p.NewProductAsync();
        var draft = (await _p.Clerk.SalesOrders.CreateAsync(new SalesOrderBuilder(_p.Warehouse, fixture.Reference.CustomerId)
            .WithLine(product.Id, 1m, 10m).WithLine(product.Id, 2m, 0m).WithLine(product.Id, 3m, 0m).Build())).Data<SalesOrderDto>();

        var problem = (await _p.Clerk.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Contains("Lines 2, 3 have no price", problem.Detail);
        var after = await _p.ReadAsync(draft.Id);
        Assert.Equal((SalesOrderStatus.Draft, draft.ConcurrencyToken), (after.Status, after.ConcurrencyToken));
        Assert.Equal(0, await AuditCountAsync(AuditActions.SalesOrderSubmitted, draft.No));
    }

    // ----- acceptance 2: approve and reject, routed by tier -----
    [Fact]
    public async Task A_supervisor_approves_a_standard_order_and_is_recorded_with_a_second_audit_row()
    {
        var submitted = await _p.SubmittedWorthAsync(6000m);

        var approved = (await _p.Supervisor.SalesOrders.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).Data<SalesOrderDto>();

        Assert.Equal((SalesOrderStatus.Approved, TestUsers.UsernameOf(Roles.Supervisor), true), (approved.Status, approved.ApprovedBy, approved.ApprovedAt is not null));
        Assert.Equal(TestUsers.UsernameOf(Roles.Clerk), approved.SubmittedBy);
        Assert.Equal(1, await AuditCountAsync(AuditActions.SalesOrderApproved, approved.No));
        Assert.Contains(DocumentActions.Cancel, approved.AllowedActions); // the Supervisor who approved it is a reviewer of its tier, so may cancel it
    }

    [Fact]
    public async Task Only_the_owner_and_the_administrator_approve_a_large_order_and_a_supervisor_is_refused_with_nothing_changed()
    {
        var submitted = await _p.SubmittedWorthAsync(25000m);

        var refused = (await _p.Supervisor.SalesOrders.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await _p.Supervisor.SalesOrders.RejectAsync(submitted.Id, "too much for me", submitted.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Contains("above your approval limit", refused.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((SalesOrderStatus.Submitted, submitted.ConcurrencyToken), ((await _p.ReadAsync(submitted.Id)).Status, (await _p.ReadAsync(submitted.Id)).ConcurrencyToken));
        Assert.Equal(SalesOrderStatus.Approved, (await _p.Owner.SalesOrders.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).Data<SalesOrderDto>().Status);
        var second = await _p.SubmittedWorthAsync(30000m);
        Assert.Equal(SalesOrderStatus.Approved, (await _p.Admin.SalesOrders.ApproveAsync(second.Id, second.ConcurrencyToken)).Data<SalesOrderDto>().Status);
    }

    [Fact]
    public async Task The_owner_reviews_only_large_orders_and_the_administrator_reviews_a_standard_one()
    {
        var standard = await _p.SubmittedWorthAsync(7000m);

        var owner = (await _p.Owner.SalesOrders.ApproveAsync(standard.Id, standard.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await _p.Owner.SalesOrders.RejectAsync(standard.Id, "not mine to review", standard.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Contains("Owners review only large", owner.Detail);
        Assert.Equal(SalesOrderStatus.Approved, (await _p.Admin.SalesOrders.ApproveAsync(standard.Id, standard.ConcurrencyToken)).Data<SalesOrderDto>().Status);
    }

    [Theory]
    [InlineData(Roles.Clerk)]
    [InlineData(Roles.WarehouseStaff)]
    public async Task A_role_without_either_approval_tier_cannot_approve_or_reject(string role)
    {
        var submitted = await _p.SubmittedWorthAsync(6000m, _p.Supervisor);
        var who = role == Roles.Clerk ? _p.Clerk : _p.Staff;

        (await who.SalesOrders.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await who.SalesOrders.RejectAsync(submitted.Id, "I would like to", submitted.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Equal(SalesOrderStatus.Submitted, (await _p.ReadAsync(submitted.Id)).Status);
    }

    [Fact]
    public async Task The_submitter_cannot_approve_or_reject_their_own_order_even_as_the_administrator_and_someone_else_can()
    {
        var draft = await _p.DraftWorthAsync(6000m, _p.Admin);
        var submitted = (await _p.Admin.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<SalesOrderDto>();

        var approve = (await _p.Admin.SalesOrders.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var reject = (await _p.Admin.SalesOrders.RejectAsync(submitted.Id, "I changed my mind", submitted.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Contains("cannot approve a sales order you submitted", approve.Detail);
        Assert.Contains("cannot reject a sales order you submitted", reject.Detail);
        Assert.Empty((await _p.ReadAsync(submitted.Id)).AllowedActions.Intersect([DocumentActions.Approve, DocumentActions.Reject]));
        Assert.Equal(SalesOrderStatus.Approved, (await _p.Supervisor.SalesOrders.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).Data<SalesOrderDto>().Status);
    }

    [Fact]
    public async Task With_self_approval_switched_on_the_submitter_may_approve_their_own_order()
    {
        await using var _ = await SettingsOverride.ApplyAsync(_p.Admin, allowSelfApproval: true);
        var submitted = await _p.SubmittedWorthAsync(6000m, _p.Supervisor);

        Assert.Contains(DocumentActions.Approve, (await _p.ReadAsync(submitted.Id, _p.Supervisor)).AllowedActions);
        Assert.Equal(SalesOrderStatus.Approved, (await _p.Supervisor.SalesOrders.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).Data<SalesOrderDto>().Status);
    }

    [Fact]
    public async Task A_rejection_returns_the_order_to_draft_with_the_reason_and_who_returned_it_and_it_can_be_edited_and_submitted_again()
    {
        var submitted = await _p.SubmittedWorthAsync(6000m);

        var returned = (await _p.Supervisor.SalesOrders.RejectAsync(submitted.Id, "  The price is too high  ", submitted.ConcurrencyToken)).Data<SalesOrderDto>();

        Assert.Equal((SalesOrderStatus.Draft, true, "The price is too high"), (returned.Status, returned.IsReturned, returned.RejectionReason));
        Assert.Equal((TestUsers.UsernameOf(Roles.Supervisor), true), (returned.ReturnedBy, returned.ReturnedAt is not null));
        Assert.Equal((false, (string?)null), (returned.RequiresApproval, returned.SubmittedBy));
        Assert.Contains(DocumentActions.Edit, (await _p.ReadAsync(returned.Id, _p.Clerk)).AllowedActions);
        var again = (await _p.Clerk.SalesOrders.SubmitAsync(returned.Id, returned.ConcurrencyToken)).Data<SalesOrderDto>();
        Assert.Equal((SalesOrderStatus.Submitted, false, (string?)null), (again.Status, again.IsReturned, again.RejectionReason));
        Assert.Equal(1, await AuditCountAsync(AuditActions.SalesOrderRejected, returned.No));
        var after = Json(await fixture.Database.ScalarAsync<string>("SELECT [After] FROM AuditLogs WHERE EntityId = @no AND Action = @a", ("@no", returned.No), ("@a", AuditActions.SalesOrderRejected)));
        Assert.Equal("The price is too high", after.GetProperty("Reason").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  no  ")]
    public async Task A_rejection_needs_a_reason_of_five_characters_and_changes_nothing_without_it(string? reason)
    {
        var submitted = await _p.SubmittedWorthAsync(6000m);

        (await _p.Supervisor.SalesOrders.RejectAsync(submitted.Id, reason!, submitted.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(SalesOrderStatus.Submitted, (await _p.ReadAsync(submitted.Id)).Status);
    }

    [Fact]
    public async Task Only_a_submitted_order_can_be_approved_or_rejected()
    {
        var draft = await _p.DraftWorthAsync(6000m);
        var released = await _p.SubmittedWorthAsync(100m);

        (await _p.Admin.SalesOrders.ApproveAsync(draft.Id, draft.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _p.Admin.SalesOrders.RejectAsync(draft.Id, "it is only a draft", draft.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _p.Supervisor.SalesOrders.ApproveAsync(released.Id, released.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _p.Supervisor.SalesOrders.RejectAsync(released.Id, "already released", released.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    // ----- recall -----
    [Fact]
    public async Task The_submitter_takes_a_submitted_order_back_to_draft_and_a_repeat_changes_nothing()
    {
        var submitted = await _p.SubmittedWorthAsync(6000m);

        var recalled = (await _p.Clerk.SalesOrders.RecallAsync(submitted.Id, submitted.ConcurrencyToken)).Data<SalesOrderDto>();
        var repeat = (await _p.Clerk.SalesOrders.RecallAsync(submitted.Id, submitted.ConcurrencyToken)).Data<SalesOrderDto>(); // the double tap holds the old token

        Assert.Equal((SalesOrderStatus.Draft, false, false, (string?)null), (recalled.Status, recalled.IsReturned, recalled.RequiresApproval, recalled.SubmittedBy));
        Assert.Equal(recalled.ConcurrencyToken, repeat.ConcurrencyToken);
        Assert.Equal(1, await AuditCountAsync(AuditActions.SalesOrderRecalled, recalled.No));
    }

    [Fact]
    public async Task Nobody_but_the_submitter_recalls_not_even_an_administrator_and_an_approved_order_is_never_recalled()
    {
        var submitted = await _p.SubmittedWorthAsync(6000m);

        (await _p.Admin.SalesOrders.RecallAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await _p.Supervisor.SalesOrders.RecallAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);
        var approved = (await _p.Supervisor.SalesOrders.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).Data<SalesOrderDto>();
        (await _p.Clerk.SalesOrders.RecallAsync(approved.Id, approved.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(SalesOrderStatus.Approved, (await _p.ReadAsync(approved.Id)).Status);
    }

    // ----- acceptance 4: cancel -----
    [Fact]
    public async Task A_draft_is_cancelled_by_its_creator_without_a_reason_and_not_by_another_manager()
    {
        var draft = await _p.DraftWorthAsync(10m);

        (await _p.Supervisor.SalesOrders.CancelAsync(draft.Id, null, draft.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);
        var cancelled = (await _p.Clerk.SalesOrders.CancelAsync(draft.Id, null, draft.ConcurrencyToken)).Data<SalesOrderDto>();

        Assert.Equal((SalesOrderStatus.Cancelled, (string?)null), (cancelled.Status, cancelled.CancelReason));
        Assert.Empty(cancelled.AllowedActions);
        Assert.Equal(1, await AuditCountAsync(AuditActions.SalesOrderCancelled, cancelled.No));
    }

    [Fact]
    public async Task A_submitted_order_is_cancelled_by_its_submitter_with_a_reason_kept_trimmed()
    {
        var submitted = await _p.SubmittedWorthAsync(6000m);

        (await _p.Supervisor.SalesOrders.CancelAsync(submitted.Id, "not mine to cancel", submitted.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);
        var cancelled = (await _p.Clerk.SalesOrders.CancelAsync(submitted.Id, "  duplicate of another order  ", submitted.ConcurrencyToken)).Data<SalesOrderDto>();

        Assert.Equal((SalesOrderStatus.Cancelled, "duplicate of another order"), (cancelled.Status, cancelled.CancelReason));
    }

    [Fact]
    public async Task An_approved_order_is_cancelled_with_a_reason_by_a_reviewer_of_its_tier_and_by_nobody_else()
    {
        var standard = await _p.SubmittedWorthAsync(100m); // released at once, a standard order
        var large = await _p.SubmittedWorthAsync(25000m);
        large = (await _p.Owner.SalesOrders.ApproveAsync(large.Id, large.ConcurrencyToken)).Data<SalesOrderDto>();

        foreach (var order in new[] { standard, large })
        {
            (await _p.Clerk.SalesOrders.CancelAsync(order.Id, "supplier closed down", order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden); // its maker included
            (await _p.Staff.SalesOrders.CancelAsync(order.Id, "supplier closed down", order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);
        }
        (await _p.Owner.SalesOrders.CancelAsync(standard.Id, "supplier closed down", standard.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await _p.Supervisor.SalesOrders.CancelAsync(large.Id, "supplier closed down", large.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Equal(SalesOrderStatus.Cancelled, (await _p.Supervisor.SalesOrders.CancelAsync(standard.Id, "supplier closed down", standard.ConcurrencyToken)).Data<SalesOrderDto>().Status);
        Assert.Equal(SalesOrderStatus.Cancelled, (await _p.Owner.SalesOrders.CancelAsync(large.Id, "supplier closed down", large.ConcurrencyToken)).Data<SalesOrderDto>().Status);
        Assert.Equal(TestUsers.UsernameOf(Roles.Owner), (await _p.ReadAsync(large.Id)).ApprovedBy); // who approved it stays as history
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no")]
    public async Task Cancelling_an_approved_order_needs_a_reason_of_five_characters(string? reason)
    {
        var approved = await _p.SubmittedWorthAsync(100m);

        (await _p.Supervisor.SalesOrders.CancelAsync(approved.Id, reason, approved.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(SalesOrderStatus.Approved, (await _p.ReadAsync(approved.Id)).Status);
    }

    [Fact]
    public async Task A_cancelled_order_is_final_nothing_more_can_be_done_with_it()
    {
        var order = await _p.DraftWorthAsync(6000m);
        var cancelled = (await _p.Clerk.SalesOrders.CancelAsync(order.Id, null, order.ConcurrencyToken)).Data<SalesOrderDto>();
        var token = cancelled.ConcurrencyToken;

        (await _p.Admin.SalesOrders.CancelAsync(order.Id, "cancel it again", token)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _p.Clerk.SalesOrders.SubmitAsync(order.Id, token)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _p.Supervisor.SalesOrders.ApproveAsync(order.Id, token)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _p.Clerk.SalesOrders.UpdateAsync(order.Id, new SalesOrderBuilder(_p.Warehouse, fixture.Reference.CustomerId).WithLine(cancelled.Lines[0].ProductId, 1m, 1m).Build(), token)).ShouldBeProblem(HttpStatusCode.Conflict);
    }

    // ----- the version token on every transition -----
    [Fact]
    public async Task Every_transition_demands_the_version_token_428_without_one_and_409_with_a_stale_one()
    {
        var draft = await _p.DraftWorthAsync(6000m);
        var submitted = (await _p.Clerk.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<SalesOrderDto>();
        var calls = new (string Action, ApiClient By, object? Body)[]
        {
            ("submit", _p.Clerk, null), ("approve", _p.Supervisor, null), ("reject", _p.Supervisor, new { reason = "send it back" }),
            ("recall", _p.Clerk, null), ("cancel", _p.Clerk, new { reason = "no longer needed" })
        };

        foreach (var (action, by, body) in calls)
            (await by.SalesOrders.TransitionWithoutIfMatchAsync(submitted.Id, action, body)).ShouldBeProblem(HttpStatusCode.PreconditionRequired);
        // the token of the draft is the token of an older version of the order now
        (await _p.Supervisor.SalesOrders.ApproveAsync(submitted.Id, draft.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);
        (await _p.Supervisor.SalesOrders.RejectAsync(submitted.Id, "send it back", draft.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);
        (await _p.Clerk.SalesOrders.RecallAsync(submitted.Id, draft.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);
        (await _p.Clerk.SalesOrders.CancelAsync(submitted.Id, "no longer needed", draft.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);
        (await _p.Clerk.SalesOrders.SubmitAsync(submitted.Id, draft.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(submitted.ConcurrencyToken, (await _p.ReadAsync(submitted.Id)).ConcurrencyToken); // nothing moved
    }

    /// <summary>Security retest F-2 (API1/API5): "If-Match: *" is no version, so it cannot approve (or move) an order; the real token still can.</summary>
    [Fact]
    public async Task The_star_is_428_on_every_transition_and_the_real_token_still_works()
    {
        var draft = await _p.DraftWorthAsync(6000m);
        var submitted = (await _p.Clerk.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<SalesOrderDto>();
        var calls = new (string Action, ApiClient By, object? Body)[]
        {
            ("approve", _p.Supervisor, null), ("reject", _p.Supervisor, new { reason = "send it back" }),
            ("recall", _p.Clerk, null), ("cancel", _p.Clerk, new { reason = "no longer needed" }), ("submit", _p.Clerk, null)
        };

        foreach (var (action, by, body) in calls)
        {
            var response = await by.SendAsync(HttpMethod.Post, $"/api/sales-orders/{submitted.Id}/{action}", body, r => r.Headers.TryAddWithoutValidation("If-Match", "*"));
            response.ShouldBeProblem(HttpStatusCode.PreconditionRequired);
            Assert.Equal("if_match_required", response.Json().RootElement.GetProperty("code").GetString());
        }

        var after = await _p.ReadAsync(submitted.Id);
        Assert.Equal((SalesOrderStatus.Submitted, submitted.ConcurrencyToken), (after.Status, after.ConcurrencyToken)); // nothing moved
        Assert.Equal(SalesOrderStatus.Approved, (await _p.Supervisor.SalesOrders.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).Data<SalesOrderDto>().Status);
    }

    // ----- acceptance 9: two approvers race -----
    [Fact]
    public async Task Two_approvers_with_the_same_version_give_one_approval_and_one_409_over_many_rounds()
    {
        for (var round = 0; round < 10; round++)
        {
            var submitted = await _p.SubmittedWorthAsync(6000m);

            var results = await Concurrently.RunAsync<ApiResponse>(
            [
                () => _p.Supervisor.SalesOrders.ApproveAsync(submitted.Id, submitted.ConcurrencyToken),
                () => _p.Admin.SalesOrders.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)
            ]);

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], results.Select(r => r.Status).Order());
            Assert.Equal(1, await AuditCountAsync(AuditActions.SalesOrderApproved, submitted.No));
            Assert.Equal(SalesOrderStatus.Approved, (await _p.ReadAsync(submitted.Id)).Status);
        }
    }

    [Fact]
    public async Task An_approval_and_a_cancellation_by_the_submitter_racing_leave_one_winner()
    {
        for (var round = 0; round < 5; round++)
        {
            var submitted = await _p.SubmittedWorthAsync(6000m);

            var results = await Concurrently.RunAsync<ApiResponse>(
            [
                () => _p.Supervisor.SalesOrders.ApproveAsync(submitted.Id, submitted.ConcurrencyToken),
                () => _p.Clerk.SalesOrders.CancelAsync(submitted.Id, "changed our mind", submitted.ConcurrencyToken)
            ]);

            Assert.Equal(1, results.Count(r => r.Status == HttpStatusCode.OK));
            Assert.All(results.Where(r => r.Status != HttpStatusCode.OK), r => Assert.Contains((int)r.Status, new[] { 400, 403, 409 }));
        }
    }

    // ----- warehouse scope -----
    [Fact]
    public async Task An_order_in_a_warehouse_outside_the_callers_scope_does_not_exist_for_every_transition()
    {
        var elsewhere = await _p.NewWarehouseAsync();
        var product = await _p.NewProductAsync();
        var draft = (await _p.Admin.SalesOrders.CreateAsync(new SalesOrderBuilder(elsewhere.Id, fixture.Reference.CustomerId).WithLine(product.Id, 1m, 25000m).Build())).Data<SalesOrderDto>();
        var submitted = (await _p.Admin.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<SalesOrderDto>(); // held for the Owner (a large order)

        // the Supervisor and the Clerk work in the default warehouse only
        (await _p.Supervisor.SalesOrders.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.NotFound);
        (await _p.Supervisor.SalesOrders.RejectAsync(submitted.Id, "not in my warehouse", submitted.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.NotFound);
        (await _p.Clerk.SalesOrders.RecallAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.NotFound);
        (await _p.Clerk.SalesOrders.CancelAsync(submitted.Id, "not in my warehouse", submitted.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.NotFound);
        (await _p.Clerk.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.NotFound);
        Assert.Equal(SalesOrderStatus.Submitted, (await _p.ReadAsync(submitted.Id)).Status);
        Assert.Equal(SalesOrderStatus.Approved, (await _p.Owner.SalesOrders.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).Data<SalesOrderDto>().Status); // the Owner sees every warehouse and holds the large tier
    }
}

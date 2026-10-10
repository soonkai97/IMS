using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>BR-06 / BR-07: approval rules by amount and quantity, who may approve, and the rule that you cannot approve your own submission.</summary>
[Collection(ApiCollection.Name)]
public class ApprovalTests(ApiFixture fixture)
{
    private int Warehouse => fixture.Reference.DefaultWarehouseId;

    /// <summary>A product with 100 units on hand at cost 100, so an issue of 60 is worth 6000 (above the seeded 5000 limit).</summary>
    private async Task<(StockScenario Scenario, ProductDto Product)> ExpensiveStockAsync()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 100m));
        return (s, product);
    }

    private async Task<DocumentDto> SubmitBigIssueAsync(StockScenario s, ProductDto product, ApiClient? by = null)
    {
        by ??= s.Clerk;
        return await s.SubmitAsync(by, await s.DraftIssueAsync(by, Warehouse, new Line(product.Id, 60)));
    }

    // ---------- when approval is needed ----------

    [Fact]
    public async Task An_issue_worth_at_least_the_limit_needs_approval_and_a_smaller_one_does_not()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 200, 50m)); // average cost 50, limit 5000 = 100 units

        var atLimit = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 100)));
        var belowLimit = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 99)));

        Assert.True(atLimit.RequiresApproval);
        Assert.False(belowLimit.RequiresApproval);
    }

    [Fact]
    public async Task A_receipt_never_needs_approval_under_the_default_rules()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();

        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 10, 100_000m)));

        Assert.False(submitted.RequiresApproval);
    }

    [Fact]
    public async Task A_quantity_rule_asks_for_approval_at_the_threshold_and_stops_when_deactivated()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 500, 1m)); // cheap: the amount rule never fires

        await using (await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Issue, null, 50, Roles.Supervisor))
        {
            var atThreshold = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 50)));
            var below = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 49)));
            Assert.True(atThreshold.RequiresApproval);
            Assert.False(below.RequiresApproval);
        }

        var afterwards = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 60)));
        Assert.False(afterwards.RequiresApproval);
    }

    [Fact]
    public async Task A_rule_for_issues_does_not_affect_receipts_and_the_quantity_is_counted_in_base_units()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync(customize: b => b.WithConversion(fixture.Reference.CartonUnitId, 12));
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 1000, 1m));

        await using var _ = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Issue, null, 100, Roles.Supervisor);
        var receipt = await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 500, 1m)));
        var tenCartons = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 10, UomId: fixture.Reference.CartonUnitId)));

        Assert.False(receipt.RequiresApproval);
        Assert.True(tenCartons.RequiresApproval); // 10 cartons = 120 pieces >= 100
    }

    // ---------- who may approve ----------

    [Fact]
    public async Task The_owner_approves_a_large_issue_and_the_admin_then_posts_it()
    {
        var (s, product) = await ExpensiveStockAsync();
        await using var _ = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Issue, 6000m, null, Roles.Owner); // 60 x 100 is worth exactly 6000
        var submitted = await SubmitBigIssueAsync(s, product);
        Assert.True(submitted.RequiresApproval);
        Assert.True(submitted.IsLarge);

        var approved = (await s.Owner.Documents.ApproveAsync(submitted.Id)).Data<DocumentDto>();
        var posted = (await s.Admin.Documents.PostAsync(submitted.Id)).Data<DocumentDto>();

        Assert.Equal(DocumentStatus.Approved, approved.Status);
        Assert.Equal("owner", approved.ApprovedBy);
        Assert.NotNull(approved.ApprovedAt);
        Assert.Equal(DocumentStatus.Posted, posted.Status);
        Assert.Equal(40, await s.OnHandAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task A_supervisor_can_approve_when_the_rule_asks_for_a_supervisor()
    {
        var (s, product) = await ExpensiveStockAsync();
        var submitted = await SubmitBigIssueAsync(s, product);

        var approved = (await s.Supervisor.Documents.ApproveAsync(submitted.Id)).Data<DocumentDto>();

        Assert.Equal(DocumentStatus.Approved, approved.Status);
    }

    [Fact]
    public async Task A_document_that_needs_no_override_is_approved_without_a_reason_and_ignores_one()
    {
        var (s, product) = await ExpensiveStockAsync();
        var withoutBody = await SubmitBigIssueAsync(s, product);
        var withReason = await SubmitBigIssueAsync(s, product);
        Assert.False(withoutBody.RequiresOverride);

        var plain = (await s.Supervisor.Documents.ApproveAsync(withoutBody.Id)).Data<DocumentDto>();
        var ignored = (await s.Supervisor.Documents.ApproveWithReasonAsync(withReason.Id, "not needed here")).Data<DocumentDto>();

        Assert.Equal(DocumentStatus.Approved, plain.Status);
        Assert.Null(plain.NegativeStockOverrideReason);
        Assert.Equal(DocumentStatus.Approved, ignored.Status);
        Assert.Null(ignored.NegativeStockOverrideReason);
    }

    [Fact]
    public async Task A_rule_that_names_the_owner_cannot_be_approved_by_a_supervisor()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 500, 1m));
        await using var _ = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Issue, null, 400, Roles.Owner);
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 400)));

        var refused = await s.Supervisor.Documents.ApproveAsync(submitted.Id);

        Assert.Contains("above your approval limit", refused.ShouldBeProblem(HttpStatusCode.Forbidden).Detail);
        Assert.Equal(DocumentStatus.Submitted, (await s.GetAsync(submitted.Id)).Status);
        (await s.Owner.Documents.ApproveAsync(submitted.Id)).ShouldBeOk();
    }

    [Fact]
    public async Task The_submitter_cannot_approve_their_own_document_but_someone_else_can()
    {
        var (s, product) = await ExpensiveStockAsync();
        var submitted = await SubmitBigIssueAsync(s, product, by: s.Supervisor);

        var self = await s.Supervisor.Documents.ApproveAsync(submitted.Id);

        Assert.Contains("cannot approve a document you submitted", self.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(DocumentStatus.Submitted, (await s.GetAsync(submitted.Id)).Status);
        (await s.Admin.Documents.ApproveAsync(submitted.Id)).ShouldBeOk();
    }

    [Fact]
    public async Task The_setting_can_allow_self_approval()
    {
        var (s, product) = await ExpensiveStockAsync();
        var submitted = await SubmitBigIssueAsync(s, product, by: s.Supervisor);

        await using var _ = await SettingsOverride.ApplyAsync(s.Admin, allowSelfApproval: true);
        var approved = (await s.Supervisor.Documents.ApproveAsync(submitted.Id)).Data<DocumentDto>();

        Assert.Equal(DocumentStatus.Approved, approved.Status);
    }

    // ---------- posting around approval ----------

    [Fact]
    public async Task Warehouse_staff_cannot_post_a_document_that_needs_approval_before_or_after_approval()
    {
        var (s, product) = await ExpensiveStockAsync();
        var submitted = await SubmitBigIssueAsync(s, product);

        (await s.Staff.Documents.PostAsync(submitted.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await s.Supervisor.Documents.ApproveAsync(submitted.Id)).ShouldBeOk();
        (await s.Staff.Documents.PostAsync(submitted.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Equal(DocumentStatus.Approved, (await s.GetAsync(submitted.Id)).Status);
        Assert.Equal(100, await s.OnHandAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task A_supervisor_cannot_post_a_document_that_is_still_waiting_for_approval()
    {
        var (s, product) = await ExpensiveStockAsync();
        var submitted = await SubmitBigIssueAsync(s, product);

        var response = await s.Supervisor.Documents.PostAsync(submitted.Id);

        Assert.Contains("requires approval", response.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(100, await s.OnHandAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task Approving_a_document_that_does_not_need_approval_is_400()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1, 1m)));

        var response = await s.Supervisor.Documents.ApproveAsync(submitted.Id);

        Assert.Contains("does not need approval", response.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
    }

    [Fact]
    public async Task Only_a_submitted_document_can_be_approved_or_rejected()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1, 1m));

        (await s.Supervisor.Documents.ApproveAsync(draft.Id)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await s.Supervisor.Documents.RejectAsync(draft.Id, "no longer needed")).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    // ---------- rejection ----------

    [Fact]
    public async Task Rejection_returns_the_document_to_draft_with_the_reason_and_it_can_be_corrected_and_resubmitted()
    {
        var (s, product) = await ExpensiveStockAsync();
        var submitted = await SubmitBigIssueAsync(s, product);

        var rejected = (await s.Supervisor.Documents.RejectAsync(submitted.Id, "Too many units for this order")).Data<DocumentDto>();

        Assert.Equal(DocumentStatus.Draft, rejected.Status);
        Assert.Equal("Too many units for this order", rejected.RejectionReason);
        Assert.Null(rejected.SubmittedBy);

        var fixedRequest = new DocumentBuilder(Warehouse).WithPartner(fixture.Reference.CustomerId).WithLines(new Line(product.Id, 10)).Build();
        var edited = (await s.Clerk.Documents.UpdateAsync(submitted.Id, fixedRequest)).Data<DocumentDto>();
        Assert.Equal(10, edited.TotalQty);

        var resubmitted = (await s.Clerk.Documents.SubmitAsync(submitted.Id)).Data<DocumentDto>();
        Assert.Equal(DocumentStatus.Submitted, resubmitted.Status);
        Assert.False(resubmitted.RequiresApproval); // 10 units x 100 = 1000, under the limit
        Assert.Null(resubmitted.RejectionReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rejection_without_a_reason_is_400(string reason)
    {
        var (s, product) = await ExpensiveStockAsync();
        var submitted = await SubmitBigIssueAsync(s, product);

        var response = await s.Supervisor.Documents.RejectAsync(submitted.Id, reason);

        response.ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal(DocumentStatus.Submitted, (await s.GetAsync(submitted.Id)).Status);
    }

    [Fact]
    public async Task Approve_and_reject_are_not_offered_to_the_submitter()
    {
        // Rejecting is offered only to those who may approve; the submitter does not get the buttons.
        var (s, product) = await ExpensiveStockAsync();
        var submitted = await SubmitBigIssueAsync(s, product, by: s.Supervisor);

        var asSubmitter = (await s.Supervisor.Documents.GetAsync(submitted.Id)).Data<DocumentDto>();
        var asAdmin = (await s.Admin.Documents.GetAsync(submitted.Id)).Data<DocumentDto>();

        Assert.DoesNotContain(DocumentActions.Approve, asSubmitter.AllowedActions);
        Assert.DoesNotContain(DocumentActions.Reject, asSubmitter.AllowedActions);
        Assert.Contains(DocumentActions.Approve, asAdmin.AllowedActions);
        Assert.Contains(DocumentActions.Reject, asAdmin.AllowedActions);
    }
}

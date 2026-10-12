using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>
/// PRD 3.1 / UX 7.2: a document held for approval is routed to one reviewer role. The Owner reviews only large documents (EA-02), a Supervisor
/// the rest (PS-07), the Admin all; rejecting is a review like approving, and the submitter may not review their own (EA-11).
/// </summary>
[Collection(ApiCollection.Name)]
public class ReviewRoutingTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    /// <summary>100 units at cost 100, so an issue of 60 is worth 6000 (the seeded supervisor rule fires from 5000).</summary>
    private async Task<(StockScenario S, ProductDto Product)> StockAsync()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 100m));
        return (s, product);
    }

    private async Task<DocumentDto> SubmitIssueAsync(StockScenario s, ProductDto product, ApiClient by, decimal qty = 60) =>
        await s.SubmitAsync(by, await s.DraftIssueAsync(by, Warehouse, new Line(product.Id, qty)));

    // ----- the Owner reviews only large documents -----
    [Fact]
    public async Task The_owner_cannot_approve_or_reject_a_document_that_is_not_large()
    {
        var (s, product) = await StockAsync();
        var submitted = await SubmitIssueAsync(s, product, s.Clerk);
        Assert.False(submitted.IsLarge);

        var approve = await s.Owner.Documents.ApproveAsync(submitted.Id);
        var reject = await s.Owner.Documents.RejectAsync(submitted.Id, "Please check the quantity");

        Assert.Contains("only large documents", approve.ShouldBeProblem(HttpStatusCode.Forbidden).Detail);
        reject.ShouldBeProblem(HttpStatusCode.Forbidden);
        Assert.Equal(DocumentStatus.Submitted, (await s.GetAsync(submitted.Id)).Status);
    }

    [Fact]
    public async Task The_owner_cannot_approve_an_override_document_that_is_not_large_because_that_is_a_supervisor_act()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 2m));
        await using var _ = await SettingsOverride.ApplyAsync(s.Admin, allowNegativeStock: true);
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 8)));
        Assert.True(submitted.RequiresOverride && !submitted.IsLarge);

        (await s.Owner.Documents.ApproveWithReasonAsync(submitted.Id, "Customer pickup before goods-in")).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await s.Supervisor.Documents.ApproveWithReasonAsync(submitted.Id, "Customer pickup before goods-in")).ShouldBeOk();
    }

    [Fact]
    public async Task The_owner_approves_and_rejects_a_large_document()
    {
        var (s, product) = await StockAsync();
        await using var _ = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Issue, 6000m, null, Roles.Owner);
        var toApprove = await SubmitIssueAsync(s, product, s.Clerk);
        var toReject = await SubmitIssueAsync(s, product, s.Clerk);
        Assert.True(toApprove.IsLarge && toReject.IsLarge);

        Assert.Equal(DocumentStatus.Approved, (await s.Owner.Documents.ApproveAsync(toApprove.Id)).Data<DocumentDto>().Status);
        Assert.Equal(DocumentStatus.Draft, (await s.Owner.Documents.RejectAsync(toReject.Id, "Too much for one order")).Data<DocumentDto>().Status);
    }

    // ----- the Supervisor reviews only what is not large -----
    [Fact]
    public async Task A_supervisor_can_neither_approve_nor_reject_a_large_document()
    {
        var (s, product) = await StockAsync();
        await using var _ = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Issue, 6000m, null, Roles.Owner);
        var submitted = await SubmitIssueAsync(s, product, s.Clerk);

        var approve = await s.Supervisor.Documents.ApproveAsync(submitted.Id);
        var reject = await s.Supervisor.Documents.RejectAsync(submitted.Id, "Please check the quantity");

        Assert.Contains("above your approval limit", approve.ShouldBeProblem(HttpStatusCode.Forbidden).Detail);
        reject.ShouldBeProblem(HttpStatusCode.Forbidden);
        Assert.Equal(DocumentStatus.Submitted, (await s.GetAsync(submitted.Id)).Status);
    }

    [Fact]
    public async Task The_admin_reviews_any_document()
    {
        var (s, product) = await StockAsync();
        await using var _ = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Issue, 6000m, null, Roles.Owner);
        var large = await SubmitIssueAsync(s, product, s.Clerk);

        Assert.Equal(DocumentStatus.Approved, (await s.Admin.Documents.ApproveAsync(large.Id)).Data<DocumentDto>().Status);
    }

    // ----- allowedActions and canAct tell the truth (PS-07) -----
    [Fact]
    public async Task What_allowedActions_offers_is_what_the_API_accepts_for_a_large_document()
    {
        var (s, product) = await StockAsync();
        await using var _ = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Issue, 6000m, null, Roles.Owner);
        var submitted = await SubmitIssueAsync(s, product, s.Clerk);

        var seenBySupervisor = (await s.Supervisor.Documents.GetAsync(submitted.Id)).Data<DocumentDto>();
        var seenByOwner = (await s.Owner.Documents.GetAsync(submitted.Id)).Data<DocumentDto>();

        Assert.DoesNotContain(DocumentActions.Approve, seenBySupervisor.AllowedActions);
        Assert.DoesNotContain(DocumentActions.Reject, seenBySupervisor.AllowedActions);
        Assert.Contains(DocumentActions.Approve, seenByOwner.AllowedActions);
        Assert.Contains(DocumentActions.Reject, seenByOwner.AllowedActions);
        (await s.Supervisor.Documents.ApproveAsync(submitted.Id)).ShouldBe(HttpStatusCode.Forbidden);
        (await s.Owner.Documents.ApproveAsync(submitted.Id)).ShouldBeOk();
    }

    [Fact]
    public async Task A_rule_that_names_a_higher_role_than_the_documents_flag_still_hides_approve_from_a_lower_role()
    {
        // The live approval rules can change after submission; the single-document view re-checks them, so it never offers what the API refuses.
        var (s, product) = await StockAsync();
        var submitted = await SubmitIssueAsync(s, product, s.Clerk);
        Assert.False(submitted.IsLarge);

        await using var _ = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Issue, 1m, null, Roles.Admin);
        var seen = (await s.Supervisor.Documents.GetAsync(submitted.Id)).Data<DocumentDto>();

        Assert.DoesNotContain(DocumentActions.Approve, seen.AllowedActions);
        (await s.Supervisor.Documents.ApproveAsync(submitted.Id)).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_list_says_per_row_whether_the_caller_can_act_on_it()
    {
        var (s, product) = await StockAsync();
        var ordinary = await SubmitIssueAsync(s, product, s.Clerk);
        var own = await SubmitIssueAsync(s, product, s.Supervisor);
        await using var _ = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Issue, 6000m, null, Roles.Owner);
        var large = await SubmitIssueAsync(s, product, s.Clerk);

        async Task<Dictionary<int, bool>> CanActAsync(ApiClient who) =>
            (await who.Documents.ListAsync(status: DocumentStatus.Submitted, requiresApproval: true)).Data<PagedResult<DocumentListItem>>()
                .Items.Where(i => new[] { ordinary.Id, own.Id, large.Id }.Contains(i.Id)).ToDictionary(i => i.Id, i => i.CanAct);

        var supervisor = await CanActAsync(s.Supervisor);
        var owner = await CanActAsync(s.Owner);
        var admin = await CanActAsync(s.Admin);
        var clerk = await CanActAsync(s.Clerk);

        Assert.True(supervisor[ordinary.Id]);
        Assert.False(supervisor[own.Id]);   // my own submission
        Assert.False(supervisor[large.Id]); // the Owner's
        Assert.False(owner[ordinary.Id]);
        Assert.False(owner[own.Id]);
        Assert.True(owner[large.Id]);
        Assert.True(admin[ordinary.Id] && admin[own.Id] && admin[large.Id]);
        Assert.All(clerk.Values, canAct => Assert.False(canAct));

        // Leave nothing waiting behind: other tests count what is waiting for the supervisor.
        foreach (var id in new[] { ordinary.Id, large.Id }) (await s.Admin.Documents.CancelAsync(id, "Test clean-up")).ShouldBeOk();
        (await s.Supervisor.Documents.CancelAsync(own.Id, "Test clean-up")).ShouldBeOk();
    }

    [Fact]
    public async Task The_approvals_count_follows_the_same_routing_as_canAct()
    {
        var (s, product) = await StockAsync();
        async Task<int> WaitingAsync(ApiClient who) => (await who.Documents.CountsAsync()).Data<DocumentCounts>().ApprovalsWaiting;
        var before = new[] { await WaitingAsync(s.Supervisor), await WaitingAsync(s.Owner), await WaitingAsync(s.Admin) };

        await using var _ = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Issue, 6000m, null, Roles.Owner);
        var large = await SubmitIssueAsync(s, product, s.Clerk);
        Assert.True(large.IsLarge);

        // One large document more: the Owner and the Admin have one more to act on, the Supervisor none, exactly as canAct says.
        Assert.Equal(before[0], await WaitingAsync(s.Supervisor));
        Assert.Equal(before[1] + 1, await WaitingAsync(s.Owner));
        Assert.Equal(before[2] + 1, await WaitingAsync(s.Admin));
    }

    // ----- rejecting is a review (EA-11 / PS-10) -----
    [Fact]
    public async Task The_submitter_cannot_reject_their_own_document_but_can_still_cancel_it()
    {
        var (s, product) = await StockAsync();
        var submitted = await SubmitIssueAsync(s, product, s.Supervisor);

        var reject = await s.Supervisor.Documents.RejectAsync(submitted.Id, "I changed my mind");

        Assert.Contains("cannot reject a document you submitted", reject.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(DocumentStatus.Submitted, (await s.GetAsync(submitted.Id)).Status);
        (await s.Supervisor.Documents.CancelAsync(submitted.Id, "Recalled")).ShouldBeOk();
    }

    [Fact]
    public async Task With_self_approval_allowed_the_submitter_may_reject()
    {
        var (s, product) = await StockAsync();
        var submitted = await SubmitIssueAsync(s, product, s.Supervisor);

        await using var _ = await SettingsOverride.ApplyAsync(s.Admin, allowSelfApproval: true);

        Assert.Equal(DocumentStatus.Draft, (await s.Supervisor.Documents.RejectAsync(submitted.Id, "I changed my mind")).Data<DocumentDto>().Status);
    }
}

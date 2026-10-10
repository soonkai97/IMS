using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>
/// The buttons the UI shows come from <c>allowedActions</c>. Each case builds a document in one state and asks every role what it may do,
/// with the answer written out from PRD 3.1 and the state machine (Figure 5).
/// </summary>
[Collection(ApiCollection.Name)]
public class AllowedActionsTests(ApiFixture fixture)
{
    private int Warehouse => fixture.Reference.DefaultWarehouseId;

    private static string[] Actions(params string[] actions) => actions.Order().ToArray();

    private async Task AssertActionsAsync(StockScenario s, int documentId, (string Role, string[] Expected)[] expectations)
    {
        foreach (var (role, expected) in expectations)
        {
            var document = (await s.As(role).Documents.GetAsync(documentId)).Data<DocumentDto>();
            Assert.True(expected.SequenceEqual(document.AllowedActions.Order()),
                $"{role} on a {document.Status} document: expected [{string.Join(",", expected)}] but got [{string.Join(",", document.AllowedActions.Order())}]");
        }
    }

    private async Task<(StockScenario S, int ProductId)> ScenarioWithStockAsync()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 100m));
        return (s, product.Id);
    }

    [Fact]
    public async Task Draft()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1, 1m));

        await AssertActionsAsync(s, draft.Id,
        [
            (Roles.Admin, Actions("edit", "submit", "cancel")),
            (Roles.Owner, Actions()),
            (Roles.Supervisor, Actions("edit", "submit", "cancel")),
            (Roles.WarehouseStaff, Actions()), // not their draft (EA-01)
            (Roles.Clerk, Actions("edit", "submit", "cancel"))
        ]);
    }

    [Fact]
    public async Task Submitted_without_approval_can_be_posted_by_those_who_post()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1, 1m)));

        await AssertActionsAsync(s, submitted.Id,
        [
            (Roles.Admin, Actions("cancel", "post")),
            (Roles.Owner, Actions()),
            (Roles.Supervisor, Actions("cancel", "post")),
            (Roles.WarehouseStaff, Actions("post")),
            (Roles.Clerk, Actions("cancel"))
        ]);
    }

    [Fact]
    public async Task Submitted_waiting_for_approval_offers_approve_and_reject_to_approvers_only()
    {
        var (s, productId) = await ScenarioWithStockAsync();
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(productId, 60)));
        Assert.True(submitted.RequiresApproval);

        await AssertActionsAsync(s, submitted.Id,
        [
            (Roles.Admin, Actions("cancel", "approve", "reject")),
            (Roles.Owner, Actions()),                       // not a large document: the supervisor's to review (EA-02)
            (Roles.Supervisor, Actions("cancel", "approve", "reject")),
            (Roles.WarehouseStaff, Actions()),              // only the submitter (the clerk) may recall it (EA-01)
            (Roles.Clerk, Actions("cancel"))
        ]);
    }

    [Fact]
    public async Task A_large_document_is_the_owners_to_review_and_not_the_supervisors()
    {
        var (s, productId) = await ScenarioWithStockAsync(); // 100 units at cost 100: an issue of 60 is worth 6000
        await using var _ = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Issue, 6000m, null, Roles.Owner);
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(productId, 60)));
        Assert.True(submitted.IsLarge);

        await AssertActionsAsync(s, submitted.Id,
        [
            (Roles.Admin, Actions("cancel", "approve", "reject")),
            (Roles.Owner, Actions("approve", "reject")),
            (Roles.Supervisor, Actions("cancel")),          // the API would answer 403 to approve and reject (PS-07)
            (Roles.WarehouseStaff, Actions()),
            (Roles.Clerk, Actions("cancel"))
        ]);
    }

    [Fact]
    public async Task The_submitter_does_not_get_approve_or_reject_for_their_own_document()
    {
        var (s, productId) = await ScenarioWithStockAsync();
        var submitted = await s.SubmitAsync(s.Supervisor, await s.DraftIssueAsync(s.Supervisor, Warehouse, new Line(productId, 60)));

        await AssertActionsAsync(s, submitted.Id,
        [
            (Roles.Supervisor, Actions("cancel")),
            (Roles.Admin, Actions("cancel", "approve", "reject")),
            (Roles.Owner, Actions())
        ]);
    }

    [Fact]
    public async Task Staff_and_clerk_get_actions_only_on_documents_they_created_or_submitted()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var staffDraft = await s.DraftReceiptAsync(s.Staff, Warehouse, new Line(product.Id, 1, 1m));
        var clerkSubmitted = await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1, 1m)));

        await AssertActionsAsync(s, staffDraft.Id,
        [
            (Roles.WarehouseStaff, Actions("edit", "submit", "cancel")),
            (Roles.Clerk, Actions()),
            (Roles.Supervisor, Actions("edit", "submit", "cancel")),
            (Roles.Admin, Actions("edit", "submit", "cancel"))
        ]);
        await AssertActionsAsync(s, clerkSubmitted.Id,
        [
            (Roles.Clerk, Actions("cancel")),
            (Roles.WarehouseStaff, Actions("post")),
            (Roles.Supervisor, Actions("cancel", "post"))
        ]);
    }

    [Fact]
    public async Task With_self_approval_allowed_the_submitter_gets_approve_and_reject()
    {
        var (s, productId) = await ScenarioWithStockAsync();
        var submitted = await s.SubmitAsync(s.Supervisor, await s.DraftIssueAsync(s.Supervisor, Warehouse, new Line(productId, 60)));

        await using var _ = await SettingsOverride.ApplyAsync(s.Admin, allowSelfApproval: true);

        await AssertActionsAsync(s, submitted.Id, [(Roles.Supervisor, Actions("cancel", "approve", "reject"))]);
    }

    [Fact]
    public async Task Approved_can_be_posted_by_supervisor_and_admin_but_not_by_warehouse_staff_and_cannot_be_cancelled()
    {
        var (s, productId) = await ScenarioWithStockAsync();
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(productId, 60)));
        (await s.Supervisor.Documents.ApproveAsync(submitted.Id)).ShouldBeOk();

        await AssertActionsAsync(s, submitted.Id,
        [
            (Roles.Admin, Actions("post")),
            (Roles.Owner, Actions()),
            (Roles.Supervisor, Actions("post")),
            (Roles.WarehouseStaff, Actions()),
            (Roles.Clerk, Actions())
        ]);
    }

    [Fact]
    public async Task Posted_can_be_reversed_by_supervisor_and_admin_only()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var posted = await s.ReceiveAsync(Warehouse, new Line(product.Id, 1, 1m));

        await AssertActionsAsync(s, posted.Id,
        [
            (Roles.Admin, Actions("reverse")),
            (Roles.Owner, Actions()),
            (Roles.Supervisor, Actions("reverse")),
            (Roles.WarehouseStaff, Actions()),
            (Roles.Clerk, Actions())
        ]);
    }

    [Fact]
    public async Task Reversed_and_cancelled_documents_offer_nothing_to_anyone()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var reversed = await s.ReceiveAsync(Warehouse, new Line(product.Id, 1, 1m));
        (await s.Admin.Documents.ReverseAsync(reversed.Id, "undo the receipt")).ShouldBeOk();
        var cancelled = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1, 1m));
        (await s.Clerk.Documents.CancelAsync(cancelled.Id)).ShouldBeOk();

        var everyone = Roles.All.Select(role => (role, Actions())).ToArray();
        await AssertActionsAsync(s, reversed.Id, everyone);
        await AssertActionsAsync(s, cancelled.Id, everyone);
    }

    [Fact]
    public async Task A_rejected_document_is_a_draft_again_with_the_draft_actions()
    {
        var (s, productId) = await ScenarioWithStockAsync();
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(productId, 60)));
        (await s.Supervisor.Documents.RejectAsync(submitted.Id, "Check the quantity")).ShouldBeOk();

        await AssertActionsAsync(s, submitted.Id,
        [
            (Roles.Clerk, Actions("edit", "submit", "cancel")),
            (Roles.Owner, Actions())
        ]);
    }

    [Fact]
    public async Task The_actions_in_the_response_to_a_change_describe_the_new_state()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Staff, Warehouse, new Line(product.Id, 1, 1m));

        var submitted = (await s.Staff.Documents.SubmitAsync(draft.Id)).Data<DocumentDto>();
        var posted = (await s.Staff.Documents.PostAsync(draft.Id)).Data<DocumentDto>();

        Assert.Equal(Actions("cancel", "post"), submitted.AllowedActions.Order());
        Assert.Empty(posted.AllowedActions); // staff cannot reverse
    }
}

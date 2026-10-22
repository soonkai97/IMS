using System.Net;
using System.Text.Json;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Attachments;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Domain;
using static Ims.Domain.Tests.Attachments.TestImages;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>
/// Document corrections (B21/B22, docs/design/document-corrections.md): the submitter takes a Submitted document back to Draft (the Edit button).
/// Same document, same number, same lines; no stock moves; the audit trail says who did it; a repeat is a no-op; nobody else may.
/// </summary>
[Collection(ApiCollection.Name)]
public class RecallTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    private async Task<(StockScenario S, ProductDto Product)> WithStockAsync()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 100m));
        return (s, product);
    }

    private static async Task<DocumentDto> SubmittedReceiptAsync(StockScenario s, ApiClient by, ProductDto product, decimal qty = 110) =>
        await s.SubmitAsync(by, await s.DraftReceiptAsync(by, s.Reference.DefaultWarehouseId, new Line(product.Id, qty, 1m)));

    /// <summary>An issue of 60 at an average cost of 100 is above the approval threshold, so it waits for a reviewer.</summary>
    private static async Task<DocumentDto> IssueWaitingForApprovalAsync(StockScenario s, ApiClient by, ProductDto product)
    {
        var submitted = await s.SubmitAsync(by, await s.DraftIssueAsync(by, s.Reference.DefaultWarehouseId, new Line(product.Id, 60)));
        Assert.True(submitted.RequiresApproval);
        return submitted;
    }

    // ----- success -----
    [Theory]
    [InlineData(Roles.Clerk)]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.Admin)]
    public async Task The_submitter_takes_an_approval_free_document_back_to_the_same_draft_with_the_same_number_and_lines(string role)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var submitted = await SubmittedReceiptAsync(s, s.As(role), product);

        var response = await s.As(role).Documents.RecallAsync(submitted.Id, submitted.ConcurrencyToken);

        var recalled = response.ShouldBeOk().Read<DocumentDto>();
        Assert.Equal((submitted.Id, submitted.No, DocumentStatus.Draft, false), (recalled.Id, recalled.No, recalled.Status, recalled.IsReturned));
        Assert.Null(recalled.RejectionReason ?? recalled.SubmittedBy);
        Assert.Null(recalled.SubmittedAt);
        Assert.Equal(submitted.Lines.Select(l => (l.ProductId, l.Qty)), recalled.Lines.Select(l => (l.ProductId, l.Qty)));
        Assert.Equal(["cancel", "edit", "submit"], recalled.AllowedActions.Order());
        Assert.Equal(recalled.ConcurrencyToken, response.Header("ETag")!.Trim('"'));
        Assert.NotEqual(submitted.ConcurrencyToken, recalled.ConcurrencyToken);
        Assert.Equal(0, await s.OnHandAsync(product.Id, Warehouse)); // nothing was posted, nothing is posted now
    }

    [Fact]
    public async Task A_document_waiting_for_approval_leaves_the_reviewers_list_and_count_at_once_and_counts_as_a_draft_of_its_submitter()
    {
        var (s, product) = await WithStockAsync();
        var submitted = await IssueWaitingForApprovalAsync(s, s.Clerk, product);
        var waitingBefore = (await s.Supervisor.Documents.CountsAsync()).Data<DocumentCounts>().ApprovalsWaiting;
        var draftsBefore = (await s.Clerk.Documents.CountsAsync()).Data<DocumentCounts>();
        Assert.Contains(submitted.No, await WaitingNumbersAsync(s.Supervisor));

        (await s.Clerk.Documents.RecallAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeOk();

        Assert.DoesNotContain(submitted.No, await WaitingNumbersAsync(s.Supervisor));
        Assert.Equal(waitingBefore - 1, (await s.Supervisor.Documents.CountsAsync()).Data<DocumentCounts>().ApprovalsWaiting);
        var draftsAfter = (await s.Clerk.Documents.CountsAsync()).Data<DocumentCounts>();
        Assert.Equal(draftsBefore.MyDrafts + 1, draftsAfter.MyDrafts);
        Assert.Equal(draftsBefore.ReturnedToMe, draftsAfter.ReturnedToMe); // a recall is not a rejection
        Assert.Equal(DocumentStatus.Draft, (await s.GetAsync(submitted.Id)).Status);
    }

    private static async Task<List<string>> WaitingNumbersAsync(ApiClient reviewer) =>
        (await reviewer.Documents.ListAsync(status: DocumentStatus.Submitted, requiresApproval: true)).Data<PagedResult<DocumentListItem>>().Items.Select(i => i.No).ToList();

    [Fact]
    public async Task After_a_recall_the_document_is_edited_and_submitted_again_and_the_approval_rules_are_assessed_again_with_a_new_submitter()
    {
        var (s, product) = await WithStockAsync();
        var submitted = await IssueWaitingForApprovalAsync(s, s.Clerk, product);
        var recalled = (await s.Clerk.Documents.RecallAsync(submitted.Id, submitted.ConcurrencyToken)).Data<DocumentDto>();

        var edit = new DocumentBuilder(Warehouse).WithPartner(s.Reference.CustomerId).WithLines(new Line(product.Id, 1)).Build();
        var edited = (await s.Clerk.Documents.UpdateAsync(recalled.Id, edit, recalled.ConcurrencyToken)).Data<DocumentDto>();
        var again = (await s.Supervisor.Documents.SubmitAsync(edited.Id, edited.ConcurrencyToken)).Data<DocumentDto>();

        Assert.Equal(submitted.No, again.No);
        Assert.Equal(DocumentStatus.Submitted, again.Status);
        Assert.False(again.RequiresApproval); // one unit is worth 100: no approval is needed any more
        Assert.Equal(TestUsers.UsernameOf(Roles.Supervisor), again.SubmittedBy);
        Assert.NotEqual(submitted.SubmittedAt, again.SubmittedAt);
        Assert.Equal(1, again.TotalQty);
    }

    [Fact]
    public async Task Attachments_stay_on_the_document_through_a_recall()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var submitted = await SubmittedReceiptAsync(s, s.Clerk, product, 5);
        var files = new AttachmentsClient(s.Clerk);
        var file = (await files.UploadAsync(AttachmentOwnerType.Document, submitted.Id, Jpeg())).Data<AttachmentDto>();

        (await s.Clerk.Documents.RecallAsync(submitted.Id)).ShouldBeOk();

        Assert.Equal(file.Id, Assert.Single((await files.ListAsync(AttachmentOwnerType.Document, submitted.Id)).Data<List<AttachmentDto>>()).Id);
        (await s.Clerk.DeleteAsync($"/api/attachments/{file.Id}")).ShouldBe(HttpStatusCode.NoContent); // a Draft again, so the file can be removed
    }

    // ----- who may -----
    [Theory]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.WarehouseStaff)]
    public async Task Someone_who_did_not_submit_the_document_cannot_recall_it_and_nothing_changes(string role)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var submitted = await SubmittedReceiptAsync(s, s.Clerk, product);

        var response = await s.As(role).Documents.RecallAsync(submitted.Id, submitted.ConcurrencyToken);

        Assert.Equal(Messages.OwnRecallOnly, response.ShouldBeProblem(HttpStatusCode.Forbidden).Detail);
        var after = await s.GetAsync(submitted.Id);
        Assert.Equal((DocumentStatus.Submitted, submitted.ConcurrencyToken), (after.Status, after.ConcurrencyToken));
    }

    [Fact]
    public async Task The_creator_who_did_not_submit_it_cannot_recall_it()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 5, 1m));
        var submitted = (await s.Supervisor.Documents.SubmitAsync(draft.Id)).Data<DocumentDto>();

        (await s.Clerk.Documents.RecallAsync(submitted.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);
        Assert.Equal(DocumentStatus.Submitted, (await s.GetAsync(submitted.Id)).Status);
        Assert.DoesNotContain("recall", (await s.Clerk.Documents.GetAsync(submitted.Id)).Data<DocumentDto>().AllowedActions);
    }

    [Fact]
    public async Task The_owner_has_no_right_to_create_documents_and_is_refused_at_the_door()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var submitted = await SubmittedReceiptAsync(s, s.Clerk, product);

        (await s.Owner.Documents.RecallAsync(submitted.Id)).ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.Anonymous().PostAsync($"/api/documents/{submitted.Id}/recall", null)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ----- wrong state, stale token, repeats -----
    [Theory]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Posted)]
    [InlineData(DocumentStatus.Reversed)]
    [InlineData(DocumentStatus.Cancelled)]
    public async Task A_document_that_is_no_longer_submitted_is_refused_with_its_status_named(DocumentStatus status)
    {
        var (s, product) = await WithStockAsync();
        var document = await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 5, 1m)));
        switch (status)
        {
            case DocumentStatus.Approved:
                document = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 60)));
                (await s.Supervisor.Documents.ApproveAsync(document.Id)).ShouldBeOk();
                break;
            case DocumentStatus.Posted: (await s.Admin.Documents.PostAsync(document.Id)).ShouldBeOk(); break;
            case DocumentStatus.Reversed:
                (await s.Admin.Documents.PostAsync(document.Id)).ShouldBeOk();
                (await s.Admin.Documents.ReverseAsync(document.Id, "Entered twice")).ShouldBeOk();
                break;
            case DocumentStatus.Cancelled: (await s.Clerk.Documents.CancelAsync(document.Id)).ShouldBeOk(); break;
        }

        var response = await s.Clerk.Documents.RecallAsync(document.Id); // the current token is sent, so only the state is in question

        Assert.Equal(Messages.OnlySubmittedCanRecall(document.No, status), response.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(status, (await s.GetAsync(document.Id)).Status);
    }

    [Fact]
    public async Task A_returned_draft_cannot_be_recalled_because_it_is_a_draft_already_and_stays_returned()
    {
        var (s, product) = await WithStockAsync();
        var submitted = await IssueWaitingForApprovalAsync(s, s.Clerk, product);
        (await s.Supervisor.Documents.RejectAsync(submitted.Id, "Check the quantity")).ShouldBeOk();

        var response = await s.Clerk.Documents.RecallAsync(submitted.Id);

        Assert.Equal(Messages.OnlySubmittedCanRecall(submitted.No, DocumentStatus.Draft), response.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.True((await s.GetAsync(submitted.Id)).IsReturned);
    }

    [Fact]
    public async Task Losing_the_race_to_an_approval_is_a_stale_document_409_and_the_approval_stands()
    {
        var (s, product) = await WithStockAsync();
        var submitted = await IssueWaitingForApprovalAsync(s, s.Clerk, product);
        (await s.Supervisor.Documents.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeOk();

        var response = await s.Clerk.Documents.RecallAsync(submitted.Id, submitted.ConcurrencyToken); // the page the clerk still has open

        response.ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal("document_changed", response.Json().RootElement.GetProperty("code").GetString());
        Assert.Equal(DocumentStatus.Approved, (await s.GetAsync(submitted.Id)).Status);
    }

    [Fact]
    public async Task A_second_recall_is_a_no_op_that_answers_200_with_the_draft_even_with_the_old_token_and_writes_no_second_audit_row()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var submitted = await SubmittedReceiptAsync(s, s.Clerk, product);
        var first = (await s.Clerk.Documents.RecallAsync(submitted.Id, submitted.ConcurrencyToken)).Data<DocumentDto>();

        var again = (await s.Clerk.Documents.RecallAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeOk().Read<DocumentDto>(); // a retry with the old token
        var withNew = (await s.Clerk.Documents.RecallAsync(submitted.Id, first.ConcurrencyToken)).ShouldBeOk().Read<DocumentDto>();

        Assert.All(new[] { again, withNew }, d => Assert.Equal((DocumentStatus.Draft, first.ConcurrencyToken), (d.Status, d.ConcurrencyToken)));
        Assert.Single(await RecalledEntriesAsync(s, submitted.No));
    }

    [Fact]
    public async Task The_retry_of_a_recall_by_a_supervisor_who_submitted_somebody_elses_draft_is_still_theirs_and_a_no_op()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 5, 1m));
        var submitted = (await s.Supervisor.Documents.SubmitAsync(draft.Id)).Data<DocumentDto>();
        var first = (await s.Supervisor.Documents.RecallAsync(submitted.Id, submitted.ConcurrencyToken)).Data<DocumentDto>();

        var again = (await s.Supervisor.Documents.RecallAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeOk().Read<DocumentDto>();

        Assert.Equal((DocumentStatus.Draft, first.ConcurrencyToken), (again.Status, again.ConcurrencyToken));
        Assert.Single(await RecalledEntriesAsync(s, submitted.No));
    }

    [Theory]
    [InlineData(Roles.Supervisor, Roles.Clerk)]
    [InlineData(Roles.Admin, Roles.Clerk)]
    [InlineData(Roles.WarehouseStaff, Roles.Clerk)]
    [InlineData(Roles.Clerk, Roles.WarehouseStaff)]
    public async Task Recalling_somebody_elses_draft_is_403_for_every_role_not_a_no_op_and_the_creators_own_repeat_is(string role, string creator)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var draft = await s.DraftReceiptAsync(s.As(creator), Warehouse, new Line((await s.NewProductAsync()).Id, 5, 1m));

        var refused = await s.As(role).Documents.RecallAsync(draft.Id);

        Assert.Equal(Messages.OwnRecallOnly, refused.ShouldBeProblem(HttpStatusCode.Forbidden).Detail);
        (await s.As(creator).Documents.RecallAsync(draft.Id)).ShouldBeOk(); // the creator's own plain draft: the same call is a no-op
        Assert.Empty(await RecalledEntriesAsync(s, draft.No));
    }

    [Fact]
    public async Task Somebody_elses_posted_or_cancelled_document_is_403_too_so_the_answer_does_not_tell_its_state()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var posted = await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 1m));
        var cancelled = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 5, 1m));
        (await s.Clerk.Documents.CancelAsync(cancelled.Id)).ShouldBeOk();

        (await s.Clerk.Documents.RecallAsync(posted.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await s.Supervisor.Documents.RecallAsync(cancelled.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);
    }

    // ----- audit -----
    [Fact]
    public async Task A_recall_is_audited_once_with_who_did_it_and_the_old_and_new_status()
    {
        var (s, product) = await WithStockAsync();
        var submitted = await IssueWaitingForApprovalAsync(s, s.Clerk, product);

        (await s.Clerk.Documents.RecallAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeOk();

        var entry = Assert.Single(await RecalledEntriesAsync(s, submitted.No));
        Assert.Equal(TestUsers.UsernameOf(Roles.Clerk), entry.Actor);
        Assert.Equal(AuditEntities.StockDocument, entry.EntityType);
        using var before = JsonDocument.Parse(entry.Before!);
        using var after = JsonDocument.Parse(entry.After!);
        Assert.Equal("Submitted", before.RootElement.GetProperty("Status").GetString());
        Assert.True(before.RootElement.GetProperty("RequiresApproval").GetBoolean()); // the approval request that was discarded
        Assert.Equal("Draft", after.RootElement.GetProperty("Status").GetString());
        Assert.True(Math.Abs((DateTime.UtcNow - entry.At).TotalMinutes) < 10);
    }

    [Fact]
    public async Task A_refused_recall_leaves_no_audit_row()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var submitted = await SubmittedReceiptAsync(s, s.Clerk, product);

        (await s.Supervisor.Documents.RecallAsync(submitted.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await s.Clerk.Documents.RecallAsync(submitted.Id, "not-a-token")).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Empty(await RecalledEntriesAsync(s, submitted.No));
    }

    private static async Task<List<AuditEntry>> RecalledEntriesAsync(StockScenario s, string number) =>
        (await s.Admin.Audit.QueryAsync(entityType: AuditEntities.StockDocument, entityId: number, action: AuditActions.DocumentRecalled)).Data<AuditPage>().Items.ToList();

    // ----- concurrency -----
    [Fact]
    public async Task A_recall_racing_a_post_has_exactly_one_winner_and_the_stock_agrees_with_the_ledger()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var documents = new List<DocumentDto>();
        for (var i = 0; i < 6; i++) documents.Add(await SubmittedReceiptAsync(s, s.Admin, product, 10));

        var results = await Concurrently.RunAsync(documents.SelectMany(d => new[]
        {
            (Func<Task<ApiResponse>>)(() => s.Admin.Documents.PostAsync(d.Id, d.ConcurrencyToken)),
            () => s.Admin.Documents.RecallAsync(d.Id, d.ConcurrencyToken)
        }));

        var finals = new List<DocumentDto>();
        foreach (var d in documents) finals.Add(await s.GetAsync(d.Id));
        Assert.All(finals, f => Assert.Contains(f.Status, new[] { DocumentStatus.Posted, DocumentStatus.Draft }));
        var posted = finals.Count(f => f.Status == DocumentStatus.Posted);
        // Each document: one request won (200) and the other, holding the same token, lost with 409 (stale), never both and never a 500.
        Assert.All(results.Chunk(2), pair => Assert.Single(pair, r => r.Status == HttpStatusCode.OK));
        Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Equal(HttpStatusCode.Conflict, r.Status));
        Assert.Equal(posted * 10m, await s.OnHandAsync(product.Id, Warehouse));
        Assert.Equal(posted * 10m, await s.LedgerSumAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task A_recall_racing_an_approval_has_exactly_one_winner()
    {
        var (s, product) = await WithStockAsync();
        var submitted = new List<DocumentDto>();
        for (var i = 0; i < 4; i++) submitted.Add(await IssueWaitingForApprovalAsync(s, s.Clerk, product));

        var results = await Concurrently.RunAsync(submitted.SelectMany(d => new[]
        {
            (Func<Task<ApiResponse>>)(() => s.Supervisor.Documents.ApproveAsync(d.Id, d.ConcurrencyToken)),
            () => s.Clerk.Documents.RecallAsync(d.Id, d.ConcurrencyToken)
        }));

        Assert.All(results.Chunk(2), pair => Assert.Single(pair, r => r.Status == HttpStatusCode.OK));
        Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Equal(HttpStatusCode.Conflict, r.Status));
        foreach (var d in submitted)
        {
            var final = await s.GetAsync(d.Id);
            Assert.Contains(final.Status, new[] { DocumentStatus.Approved, DocumentStatus.Draft });
            Assert.Equal(final.Status == DocumentStatus.Approved, final.ApprovedBy is not null); // never an approval left on a draft
        }
    }
}

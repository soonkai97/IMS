using System.Net;
using System.Text.Json;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Attachments;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Domain;
using static Ims.Domain.Tests.Attachments.TestImages;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>
/// Document corrections (B21/B22, docs/design/document-corrections.md): a reviewer cancels an Approved document, with a reason (PR-02). Nothing was
/// posted, so no stock moves. The approver, the routing to Owner or Supervisor and the audit trail are unchanged by it.
/// </summary>
[Collection(ApiCollection.Name)]
public class CancelApprovedTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    private async Task<(StockScenario S, ProductDto Product, DocumentDto Approved)> ApprovedIssueAsync(bool large = false)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 100m));
        await using var rule = large ? await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Issue, 6000m, null, Roles.Owner) : null;
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 60)));
        Assert.Equal(large, submitted.IsLarge);
        var approved = (await s.ApproverFor(submitted).Documents.ApproveAsync(submitted.Id, submitted.ConcurrencyToken)).Data<DocumentDto>();
        Assert.Equal(DocumentStatus.Approved, approved.Status);
        return (s, product, approved);
    }

    // ----- success -----
    [Theory]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.Admin)]
    public async Task A_reviewer_cancels_an_approved_document_with_a_reason_and_no_stock_moves(string role)
    {
        var (s, product, approved) = await ApprovedIssueAsync();

        var response = await s.As(role).Documents.CancelAsync(approved.Id, "  Customer withdrew the order ", approved.ConcurrencyToken);

        var cancelled = response.ShouldBeOk().Read<DocumentDto>();
        Assert.Equal(DocumentStatus.Cancelled, cancelled.Status);
        Assert.Equal("Customer withdrew the order", cancelled.CancelReason);
        Assert.Equal(approved.No, cancelled.No);
        Assert.Equal(approved.ApprovedBy, cancelled.ApprovedBy);     // who approved and when stays on the document as history
        Assert.Equal(approved.ApprovedAt, cancelled.ApprovedAt);
        Assert.Empty(cancelled.AllowedActions);
        Assert.Equal(cancelled.ConcurrencyToken, response.Header("ETag")!.Trim('"'));
        Assert.Equal(100, await s.OnHandAsync(product.Id, Warehouse));
        Assert.Empty(await s.LedgerAsync(sourceDocId: approved.Id));
    }

    [Fact]
    public async Task A_cancelled_approved_document_is_terminal_nothing_can_post_recall_or_cancel_it_again()
    {
        var (s, _, approved) = await ApprovedIssueAsync();
        (await s.Supervisor.Documents.CancelAsync(approved.Id, "Customer withdrew the order")).ShouldBeOk();

        (await s.Admin.Documents.PostAsync(approved.Id)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await s.Clerk.Documents.RecallAsync(approved.Id)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await s.Supervisor.Documents.CancelAsync(approved.Id, "Customer withdrew the order")).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal(DocumentStatus.Cancelled, (await s.GetAsync(approved.Id)).Status);
    }

    [Fact]
    public async Task Cancelling_an_approved_document_leaves_the_reviewers_waiting_count_alone_and_the_files_in_place()
    {
        var (s, _, approved) = await ApprovedIssueAsync();
        var files = new AttachmentsClient(s.Admin);
        var file = (await files.UploadAsync(AttachmentOwnerType.Document, approved.Id, Jpeg())).Data<AttachmentDto>();
        var waiting = (await s.Supervisor.Documents.CountsAsync()).Data<DocumentCounts>().ApprovalsWaiting; // an approved document is not waiting

        (await s.Supervisor.Documents.CancelAsync(approved.Id, "Customer withdrew the order")).ShouldBeOk();

        Assert.Equal(waiting, (await s.Supervisor.Documents.CountsAsync()).Data<DocumentCounts>().ApprovalsWaiting);
        (await files.DownloadAsync(file.Id)).ShouldBeOk();                      // still readable for the history
        (await files.UploadAsync(AttachmentOwnerType.Document, approved.Id, Jpeg())).ShouldBeProblem(HttpStatusCode.Conflict); // but a cancelled document takes no new ones
    }

    // ----- the reason -----
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public async Task Cancelling_an_approved_document_without_a_reason_is_refused_and_it_stays_approved(string? reason)
    {
        var (s, _, approved) = await ApprovedIssueAsync();

        var response = await s.Supervisor.Documents.CancelAsync(approved.Id, reason);

        Assert.Equal(Messages.CancelApprovedReasonRequired, response.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(DocumentStatus.Approved, (await s.GetAsync(approved.Id)).Status);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("abcd")]
    [InlineData("  abcd ")]
    public async Task A_reason_under_the_minimum_length_is_refused_with_the_usual_sentence_and_it_stays_approved(string reason)
    {
        var (s, _, approved) = await ApprovedIssueAsync();

        var response = await s.Supervisor.Documents.CancelAsync(approved.Id, reason);

        Assert.Equal(Messages.ReasonTooShort, response.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(DocumentStatus.Approved, (await s.GetAsync(approved.Id)).Status);
    }

    [Fact]
    public async Task A_reason_of_exactly_the_minimum_length_is_enough()
    {
        var (s, _, approved) = await ApprovedIssueAsync();

        var cancelled = (await s.Supervisor.Documents.CancelAsync(approved.Id, new string('a', Limits.ReasonMinLength))).ShouldBeOk().Read<DocumentDto>();

        Assert.Equal(DocumentStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task A_reason_over_the_maximum_length_is_a_validation_error()
    {
        var (s, _, approved) = await ApprovedIssueAsync();

        (await s.Supervisor.Documents.CancelAsync(approved.Id, new string('a', Limits.RemarkMaxLength + 1))).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal(DocumentStatus.Approved, (await s.GetAsync(approved.Id)).Status);
    }

    [Fact]
    public async Task A_draft_or_submitted_document_is_still_cancelled_without_a_reason()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 5, 1m));
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 5, 1m)));

        Assert.Equal(DocumentStatus.Cancelled, (await s.Clerk.Documents.CancelAsync(draft.Id)).Data<DocumentDto>().Status);
        Assert.Equal(DocumentStatus.Cancelled, (await s.Clerk.Documents.CancelAsync(submitted.Id)).Data<DocumentDto>().Status);
    }

    // ----- who may -----
    [Theory]
    [InlineData(Roles.Clerk)]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Owner)]
    public async Task Only_the_reviewer_the_document_is_routed_to_may_cancel_it_not_its_creator_or_the_owner_of_an_ordinary_one(string role)
    {
        var (s, _, approved) = await ApprovedIssueAsync();

        var response = await s.As(role).Documents.CancelAsync(approved.Id, "Customer withdrew the order");

        Assert.Equal(Messages.OnlyReviewerCanCancelApproved, response.ShouldBeProblem(HttpStatusCode.Forbidden).Detail);
        Assert.Equal(DocumentStatus.Approved, (await s.GetAsync(approved.Id)).Status);
    }

    [Theory]
    [InlineData(Roles.Owner, true)]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Supervisor, false)]
    public async Task A_large_approved_document_is_cancelled_by_the_owner_or_admin_and_not_by_the_supervisor(string role, bool allowed)
    {
        var (s, product, approved) = await ApprovedIssueAsync(large: true);

        var response = await s.As(role).Documents.CancelAsync(approved.Id, "Customer withdrew the order");

        if (allowed) Assert.Equal(DocumentStatus.Cancelled, response.ShouldBeOk().Read<DocumentDto>().Status);
        else Assert.Equal(Messages.OnlyReviewerCanCancelApproved, response.ShouldBeProblem(HttpStatusCode.Forbidden).Detail);
        Assert.Equal(allowed ? DocumentStatus.Cancelled : DocumentStatus.Approved, (await s.GetAsync(approved.Id)).Status);
        Assert.Equal(100, await s.OnHandAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task The_refusal_comes_before_the_reason_is_checked_so_a_caller_who_may_not_cancel_learns_nothing_from_the_reason_rule()
    {
        var (s, _, approved) = await ApprovedIssueAsync();

        (await s.Clerk.Documents.CancelAsync(approved.Id, "x")).ShouldBeProblem(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_owner_still_cannot_cancel_a_draft_or_a_submitted_document()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 5, 1m));
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 5, 1m)));

        (await s.Owner.Documents.CancelAsync(draft.Id, "Not the owner's call")).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await s.Owner.Documents.CancelAsync(submitted.Id, "Not the owner's call")).ShouldBeProblem(HttpStatusCode.Forbidden);
        Assert.Equal(DocumentStatus.Draft, (await s.GetAsync(draft.Id)).Status);
        Assert.Equal(DocumentStatus.Submitted, (await s.GetAsync(submitted.Id)).Status);
    }

    // ----- tokens and states -----
    [Fact]
    public async Task A_stale_token_is_a_409_and_the_document_stays_approved()
    {
        var (s, _, approved) = await ApprovedIssueAsync();
        (await s.Admin.Documents.PostAsync(approved.Id, approved.ConcurrencyToken)).ShouldBeOk();

        var response = await s.Supervisor.Documents.CancelAsync(approved.Id, "Customer withdrew the order", approved.ConcurrencyToken);

        response.ShouldBeProblem(HttpStatusCode.Conflict);
        Assert.Equal(DocumentStatus.Posted, (await s.GetAsync(approved.Id)).Status);
    }

    [Fact]
    public async Task A_posted_document_still_cannot_be_cancelled_only_reversed_and_the_message_names_the_new_rule()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var posted = await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 1m));

        var response = await s.Admin.Documents.CancelAsync(posted.Id, "Entered by mistake");

        Assert.Equal(Messages.OnlyDraftSubmittedOrApprovedCanCancel(DocumentStatus.Posted), response.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        Assert.Equal(5, await s.OnHandAsync(product.Id, Warehouse));
    }

    // ----- audit -----
    [Fact]
    public async Task Cancelling_an_approved_document_is_audited_with_the_reason_and_the_approved_status_as_before()
    {
        var (s, _, approved) = await ApprovedIssueAsync();

        (await s.Supervisor.Documents.CancelAsync(approved.Id, "Customer withdrew the order")).ShouldBeOk();

        var entries = (await s.Admin.Audit.QueryAsync(entityType: AuditEntities.StockDocument, entityId: approved.No, action: AuditActions.DocumentCancelled)).Data<AuditPage>().Items;
        var entry = Assert.Single(entries);
        Assert.Equal(TestUsers.UsernameOf(Roles.Supervisor), entry.Actor);
        using var before = JsonDocument.Parse(entry.Before!);
        using var after = JsonDocument.Parse(entry.After!);
        Assert.Equal("Approved", before.RootElement.GetProperty("Status").GetString());
        Assert.Equal("Cancelled", after.RootElement.GetProperty("Status").GetString());
        Assert.Equal("Customer withdrew the order", after.RootElement.GetProperty("Reason").GetString());
    }

    [Fact]
    public async Task A_refused_cancel_of_an_approved_document_leaves_no_audit_row()
    {
        var (s, _, approved) = await ApprovedIssueAsync();

        (await s.Clerk.Documents.CancelAsync(approved.Id, "Customer withdrew the order")).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await s.Supervisor.Documents.CancelAsync(approved.Id, "no")).ShouldBeProblem(HttpStatusCode.BadRequest);

        var entries = (await s.Admin.Audit.QueryAsync(entityType: AuditEntities.StockDocument, entityId: approved.No, action: AuditActions.DocumentCancelled)).Data<AuditPage>().Items;
        Assert.Empty(entries);
    }

    // ----- concurrency -----
    [Fact]
    public async Task A_cancel_racing_a_post_has_exactly_one_winner_and_the_stock_agrees_with_the_ledger()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await using var rule = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Receipt, 1m, null, Roles.Supervisor); // every receipt waits for a reviewer
        var approved = new List<DocumentDto>();
        for (var i = 0; i < 6; i++)
        {
            var submitted = await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 10, 1m)));
            Assert.True(submitted.RequiresApproval);
            (await s.Supervisor.Documents.ApproveAsync(submitted.Id)).ShouldBeOk();
            approved.Add(submitted);
        }

        var results = await Concurrently.RunAsync(approved.SelectMany(d => new[]
        {
            (Func<Task<ApiResponse>>)(() => s.Admin.Documents.PostAsync(d.Id)),
            () => s.Admin.Documents.CancelAsync(d.Id, "Customer withdrew the order")
        }));

        Assert.All(results.Chunk(2), pair => Assert.Single(pair, r => r.Status == HttpStatusCode.OK));
        Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Contains(r.Status, new[] { HttpStatusCode.Conflict, HttpStatusCode.BadRequest }));
        var posted = 0;
        foreach (var d in approved)
        {
            var final = await s.GetAsync(d.Id);
            Assert.Contains(final.Status, new[] { DocumentStatus.Posted, DocumentStatus.Cancelled });
            if (final.Status == DocumentStatus.Posted) posted++;
        }
        Assert.Equal(posted * 10m, await s.OnHandAsync(product.Id, Warehouse));
        Assert.Equal(posted * 10m, await s.LedgerSumAsync(product.Id, Warehouse));
    }
}

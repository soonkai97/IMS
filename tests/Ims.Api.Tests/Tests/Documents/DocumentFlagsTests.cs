using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>
/// What a screen needs to draw a document without a second request: who created, submitted and returned it, the flags that
/// decide its badges (approval, large, override, returned), and the filters behind "Mine" and the approvals inbox.
/// </summary>
[Collection(ApiCollection.Name)]
public class DocumentFlagsTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    private async Task<DocumentListItem> ListItemAsync(ApiClient client, DocumentDto document) =>
        Assert.Single((await client.Documents.ListAsync(search: document.No)).Data<PagedResult<DocumentListItem>>().Items);

    // ----- who -----
    [Fact]
    public async Task Creator_and_submitter_carry_id_user_name_and_display_name_on_the_document_and_in_the_list()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var account = await new AccountFactory(fixture).CreateAsync(Roles.Clerk);
        var (clerk, _) = await new AccountFactory(fixture).SignInAsync(account);
        var product = await s.NewProductAsync();

        var draft = await s.DraftReceiptAsync(clerk, Warehouse, new Line(product.Id, 1, 1m));
        Assert.Equal((account.Id, account.Username, account.Username), (draft.CreatedById, draft.CreatedBy, draft.CreatedByName));
        Assert.Null(draft.SubmittedById);
        Assert.Null(draft.SubmittedByName);

        var submitted = await s.SubmitAsync(clerk, draft);
        Assert.Equal((account.Id, account.Username, account.Username), (submitted.SubmittedById, submitted.SubmittedBy, submitted.SubmittedByName));

        var item = await ListItemAsync(s.Admin, submitted);
        Assert.Equal((account.Id, account.Username, account.Username), (item.CreatedById, item.CreatedBy, item.CreatedByName));
        Assert.Equal((account.Id, account.Username, account.Username), (item.SubmittedById, item.SubmittedBy, item.SubmittedByName));
        Assert.Equal(submitted.SubmittedAt, item.SubmittedAt);
    }

    [Fact]
    public async Task Approver_and_poster_have_a_display_name_next_to_the_user_name()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var posted = await s.ReceiveAsync(Warehouse, new Line(product.Id, 1, 1m));

        Assert.False(string.IsNullOrWhiteSpace(posted.PostedBy));
        Assert.False(string.IsNullOrWhiteSpace(posted.PostedByName));
    }

    // ----- mine -----
    [Fact]
    public async Task Mine_lists_only_documents_the_caller_created()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var factory = new AccountFactory(fixture);
        var (alice, _) = await factory.SignInAsync(await factory.CreateAsync(Roles.Clerk));
        var (bob, _) = await factory.SignInAsync(await factory.CreateAsync(Roles.WarehouseStaff));
        var product = await s.NewProductAsync();
        var aliceDraft = await s.DraftReceiptAsync(alice, Warehouse, new Line(product.Id, 1, 1m));
        var bobDraft = await s.DraftReceiptAsync(bob, Warehouse, new Line(product.Id, 1, 1m));

        var mine = (await alice.Documents.ListAsync(mine: true)).Data<PagedResult<DocumentListItem>>();
        var all = (await alice.Documents.ListAsync()).Data<PagedResult<DocumentListItem>>();

        Assert.Contains(mine.Items, i => i.Id == aliceDraft.Id);
        Assert.DoesNotContain(mine.Items, i => i.Id == bobDraft.Id);
        Assert.All(mine.Items, i => Assert.Equal(aliceDraft.CreatedById, i.CreatedById));
        Assert.Contains(all.Items, i => i.Id == bobDraft.Id);
        Assert.True(all.Total > mine.Total);
    }

    [Fact]
    public async Task Mine_false_is_the_same_as_not_filtering()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var others = await s.DraftReceiptAsync(s.Staff, Warehouse, new Line(product.Id, 1, 1m));

        var page = (await s.Clerk.Documents.ListAsync(mine: false)).Data<PagedResult<DocumentListItem>>();

        Assert.Contains(page.Items, i => i.Id == others.Id);
    }

    // ----- requires approval -----
    [Fact]
    public async Task RequiresApproval_filters_the_approvals_inbox_and_the_flag_is_on_the_list_item()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await using var rule = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Receipt, null, 500m, Roles.Supervisor);
        var big = await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 600, 1m)));
        var small = await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1, 1m)));
        Assert.True(big.RequiresApproval);
        Assert.False(small.RequiresApproval);

        var inbox = (await s.Supervisor.Documents.ListAsync(status: DocumentStatus.Submitted, requiresApproval: true)).Data<PagedResult<DocumentListItem>>();
        var direct = (await s.Supervisor.Documents.ListAsync(status: DocumentStatus.Submitted, requiresApproval: false)).Data<PagedResult<DocumentListItem>>();

        Assert.Contains(inbox.Items, i => i.Id == big.Id && i.RequiresApproval);
        Assert.DoesNotContain(inbox.Items, i => i.Id == small.Id);
        Assert.All(inbox.Items, i => Assert.True(i.RequiresApproval));
        Assert.Contains(direct.Items, i => i.Id == small.Id && !i.RequiresApproval);
        Assert.DoesNotContain(direct.Items, i => i.Id == big.Id);
    }

    [Fact]
    public async Task A_draft_has_no_approval_flags()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();

        var draft = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1, 1m));
        var item = await ListItemAsync(s.Admin, draft);

        Assert.False(draft.RequiresApproval || draft.IsLarge || draft.RequiresOverride || draft.IsReturned);
        Assert.False(item.RequiresApproval || item.IsLarge || item.RequiresOverride || item.IsReturned);
    }

    // ----- large -----
    [Fact]
    public async Task IsLarge_is_set_only_when_the_rule_that_fired_needs_more_than_a_supervisor()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await using var supervisorRule = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Receipt, null, 500m, Roles.Supervisor);
        await using var ownerRule = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Receipt, null, 900m, Roles.Owner);

        var medium = await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 600, 1m)));
        var large = await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1000, 1m)));

        Assert.Equal((true, false), (medium.RequiresApproval, medium.IsLarge));
        Assert.Equal((true, true), (large.RequiresApproval, large.IsLarge));
        Assert.False((await ListItemAsync(s.Admin, medium)).IsLarge);
        Assert.True((await ListItemAsync(s.Admin, large)).IsLarge);
    }

    // ----- returned -----
    [Fact]
    public async Task A_rejected_document_says_who_returned_it_and_when_until_it_is_submitted_again()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var factory = new AccountFactory(fixture);
        var (clerk, _) = await factory.SignInAsync(await factory.CreateAsync(Roles.Clerk));
        var reviewer = await factory.CreateAsync(Roles.Supervisor);
        var (supervisor, _) = await factory.SignInAsync(reviewer);
        var product = await s.NewProductAsync();
        var submitted = await s.SubmitAsync(clerk, await s.DraftReceiptAsync(clerk, Warehouse, new Line(product.Id, 1, 1m)));
        var before = DateTime.UtcNow.AddSeconds(-5);

        var returned = (await supervisor.Documents.RejectAsync(submitted.Id, "Wrong supplier")).Data<DocumentDto>();

        Assert.Equal(DocumentStatus.Draft, returned.Status);
        Assert.True(returned.IsReturned);
        Assert.Equal("Wrong supplier", returned.RejectionReason);
        Assert.Equal((reviewer.Id, reviewer.Username, reviewer.Username), (returned.ReturnedById, returned.ReturnedBy, returned.ReturnedByName));
        Assert.InRange(returned.ReturnedAt!.Value, before, DateTime.UtcNow.AddSeconds(5));
        Assert.True((await ListItemAsync(s.Admin, returned)).IsReturned);

        var again = await s.SubmitAsync(clerk, returned);

        Assert.False(again.IsReturned);
        Assert.Null(again.RejectionReason);
        Assert.Null(again.ReturnedById);
        Assert.Null(again.ReturnedBy);
        Assert.Null(again.ReturnedByName);
        Assert.Null(again.ReturnedAt);
    }

    [Fact]
    public async Task Rejecting_resets_the_submit_time_flags_of_the_returned_draft()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await using var rule = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Receipt, null, 500m, Roles.Owner);
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 600, 1m)));
        Assert.True(submitted.RequiresApproval && submitted.IsLarge);

        var returned = (await s.Owner.Documents.RejectAsync(submitted.Id, "Too much")).Data<DocumentDto>();

        Assert.False(returned.RequiresApproval || returned.IsLarge);
    }

    // ----- override -----
    [Fact]
    public async Task An_issue_submitted_below_zero_stock_requires_an_override_and_keeps_the_flag_after_posting()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 2m));
        await using var _ = await SettingsOverride.ApplyAsync(s.Admin, allowNegativeStock: true);

        var covered = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 3)));
        var short_ = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 8)));

        Assert.False(covered.RequiresOverride);
        Assert.True(short_.RequiresOverride);
        Assert.True(short_.RequiresApproval); // an override always goes to an approver, whatever the value rules say
        Assert.Null(short_.NegativeStockOverrideReason);
        Assert.True((await ListItemAsync(s.Admin, short_)).RequiresOverride);

        var approved = (await s.Supervisor.Documents.ApproveWithReasonAsync(short_.Id, "Customer pickup before goods-in")).Data<DocumentDto>();
        Assert.True(approved.RequiresOverride);
        Assert.Equal("Customer pickup before goods-in", approved.NegativeStockOverrideReason);

        var posted = (await s.Supervisor.Documents.PostAsync(short_.Id)).Data<DocumentDto>();
        Assert.True(posted.RequiresOverride);
        Assert.Equal("Customer pickup before goods-in", posted.NegativeStockOverrideReason);
    }

    [Fact]
    public async Task A_receipt_never_requires_an_override()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await using var _ = await SettingsOverride.ApplyAsync(s.Admin, allowNegativeStock: true);

        var receipt = await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 8, 1m)));

        Assert.False(receipt.RequiresOverride);
    }

    // ----- available stock on the lines -----
    [Fact]
    public async Task Each_line_of_an_open_document_shows_what_is_on_hand_now_in_base_units()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var stocked = await s.NewProductAsync();
        var empty = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(stocked.Id, 12, 1m));

        var draft = await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(stocked.Id, 5), new Line(empty.Id, 1));

        Assert.Equal(12m, draft.Lines.Single(l => l.ProductId == stocked.Id).Available);
        Assert.Equal(0m, draft.Lines.Single(l => l.ProductId == empty.Id).Available);
        Assert.Equal(12m, (await s.Clerk.Documents.GetAsync(draft.Id)).Data<DocumentDto>().Lines.First(l => l.ProductId == stocked.Id).Available);
    }

    [Fact]
    public async Task Available_follows_the_stock_while_the_document_waits()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 1m));
        var draft = await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 2));
        await s.IssueAsync(Warehouse, new Line(product.Id, 4));

        var reloaded = await s.GetAsync(draft.Id);

        Assert.Equal(6m, reloaded.Lines.Single().Available);
    }

    [Fact]
    public async Task Available_is_null_once_the_document_is_posted_or_cancelled()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 1m));

        var posted = await s.IssueAsync(Warehouse, new Line(product.Id, 4));
        var cancelled = (await s.Clerk.Documents.CancelAsync((await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 1))).Id)).Data<DocumentDto>();

        Assert.Null(posted.Lines.Single().Available);
        Assert.Null(cancelled.Lines.Single().Available);
    }

    [Fact]
    public async Task Available_is_published_as_a_number_or_null_on_the_wire()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 3, 1m));
        var draft = await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 1));

        using var json = (await s.Clerk.Documents.GetAsync(draft.Id)).Json();

        var line = json.RootElement.GetProperty("lines")[0];
        Assert.Equal(System.Text.Json.JsonValueKind.Number, line.GetProperty("available").ValueKind);
    }
}

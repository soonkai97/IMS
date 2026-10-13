using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>
/// BR-03: stock may go below zero only when the setting allows it, an approver gives a reason when approving, and a supervisor (or higher) posts.
/// The reason is entered at approval, stored on the document, and never taken from the person who posts.
/// </summary>
[Collection(ApiCollection.Name)]
public class NegativeStockTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    /// <summary>A product with 5 on hand and an issue of 8 submitted by the clerk while negative stock is allowed. Disposing restores the setting.</summary>
    private sealed class ShortIssue(StockScenario scenario, ProductDto product, DocumentDto submitted, IAsyncDisposable setting) : IAsyncDisposable
    {
        public StockScenario S { get; } = scenario;
        public ProductDto Product { get; } = product;
        public DocumentDto Submitted { get; } = submitted;
        public ValueTask DisposeAsync() => setting.DisposeAsync();
    }

    private async Task<ShortIssue> ShortIssueAsync()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 2m));
        var setting = await SettingsOverride.ApplyAsync(s.Admin, allowNegativeStock: true);
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 8)));
        return new ShortIssue(s, product, submitted, setting);
    }

    [Fact]
    public async Task With_the_setting_on_an_issue_above_stock_can_be_submitted()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 2m));
        var draft = await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 8));

        await using (await SettingsOverride.ApplyAsync(s.Admin, allowNegativeStock: true))
            Assert.Equal(DocumentStatus.Submitted, (await s.Clerk.Documents.SubmitAsync(draft.Id)).Data<DocumentDto>().Status);

        // Setting restored: the same situation is blocked again.
        var another = await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 8));
        (await s.Clerk.Documents.SubmitAsync(another.Id)).ShouldBeProblem(HttpStatusCode.Conflict, "Insufficient stock");
    }

    [Fact]
    public async Task An_issue_below_zero_stock_always_goes_to_an_approver_whatever_the_value_rules_say()
    {
        await using var c = await ShortIssueAsync();

        var seenBySupervisor = (await c.S.Supervisor.Documents.GetAsync(c.Submitted.Id)).Data<DocumentDto>();

        Assert.True(c.Submitted.RequiresOverride);
        Assert.True(c.Submitted.RequiresApproval);
        Assert.Null(c.Submitted.NegativeStockOverrideReason);
        Assert.Contains(DocumentActions.Approve, seenBySupervisor.AllowedActions);
        Assert.DoesNotContain(DocumentActions.Post, seenBySupervisor.AllowedActions);
    }

    [Fact]
    public async Task Warehouse_staff_can_neither_approve_nor_post_the_override()
    {
        await using var c = await ShortIssueAsync();
        var s = c.S;

        (await s.Staff.Documents.PostAsync(c.Submitted.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await s.Staff.Documents.ApproveWithReasonAsync(c.Submitted.Id, "I need it")).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await s.Supervisor.Documents.ApproveWithReasonAsync(c.Submitted.Id, "Customer pickup before goods-in")).ShouldBeOk();
        (await s.Staff.Documents.PostAsync(c.Submitted.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Equal(5, await s.OnHandAsync(c.Product.Id, Warehouse));
        Assert.Equal(DocumentStatus.Approved, (await s.GetAsync(c.Submitted.Id)).Status);
    }

    [Fact]
    public async Task Posting_a_submitted_override_is_refused_until_it_has_been_approved()
    {
        await using var c = await ShortIssueAsync();

        var response = await c.S.Supervisor.Documents.PostAsync(c.Submitted.Id);

        Assert.Contains("approval", response.ShouldBeProblem(HttpStatusCode.BadRequest).Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, await c.S.OnHandAsync(c.Product.Id, Warehouse));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task The_approver_must_give_a_reason_for_an_override_and_the_document_stays_submitted(string? reason)
    {
        await using var c = await ShortIssueAsync();

        var refused = reason is null
            ? await c.S.Supervisor.Documents.ApproveAsync(c.Submitted.Id)
            : await c.S.Supervisor.Documents.ApproveWithReasonAsync(c.Submitted.Id, reason);

        Assert.Contains("reason", refused.ShouldBeProblem(HttpStatusCode.BadRequest).Detail, StringComparison.OrdinalIgnoreCase);
        var after = await c.S.GetAsync(c.Submitted.Id);
        Assert.Equal(DocumentStatus.Submitted, after.Status);
        Assert.Null(after.ApprovedBy);
        Assert.Null(after.NegativeStockOverrideReason);
    }

    [Fact]
    public async Task The_override_reason_is_limited_to_the_same_length_as_other_reasons()
    {
        await using var c = await ShortIssueAsync();

        var tooLong = await c.S.Supervisor.Documents.ApproveWithReasonAsync(c.Submitted.Id, new string('x', Limits.RemarkMaxLength + 1));
        var atLimit = await c.S.Supervisor.Documents.ApproveWithReasonAsync(c.Submitted.Id, new string('x', Limits.RemarkMaxLength));

        Assert.Contains("negativeStockOverrideReason", tooLong.ShouldBeProblem(HttpStatusCode.BadRequest).Errors!.Keys);
        Assert.Equal(Limits.RemarkMaxLength, atLimit.Data<DocumentDto>().NegativeStockOverrideReason!.Length);
    }

    [Fact]
    public async Task An_approver_gives_the_reason_then_a_supervisor_posts_below_zero_with_the_reason_recorded()
    {
        await using var c = await ShortIssueAsync();
        var s = c.S;

        var approved = (await s.Supervisor.Documents.ApproveWithReasonAsync(c.Submitted.Id, "  Customer pickup before goods-in  ")).Data<DocumentDto>();

        Assert.Equal(DocumentStatus.Approved, approved.Status);
        Assert.Equal("supervisor", approved.ApprovedBy);
        Assert.True(approved.RequiresOverride);
        Assert.Equal("Customer pickup before goods-in", approved.NegativeStockOverrideReason);
        Assert.Equal(5, await s.OnHandAsync(c.Product.Id, Warehouse));

        var posted = (await s.Supervisor.Documents.PostAsync(c.Submitted.Id)).Data<DocumentDto>();

        Assert.Equal(DocumentStatus.Posted, posted.Status);
        Assert.Equal("Customer pickup before goods-in", posted.NegativeStockOverrideReason);
        Assert.Equal(-3, await s.OnHandAsync(c.Product.Id, Warehouse));
        Assert.Equal(-3m, await s.LedgerSumAsync(c.Product.Id, Warehouse));
    }

    [Fact]
    public async Task Any_stock_reader_sees_the_stored_reason_on_the_document()
    {
        await using var c = await ShortIssueAsync();
        (await c.S.Supervisor.Documents.ApproveWithReasonAsync(c.Submitted.Id, "Customer pickup before goods-in")).ShouldBeOk();

        var seenByClerk = (await c.S.Clerk.Documents.GetAsync(c.Submitted.Id)).Data<DocumentDto>();
        var seenByStaff = (await c.S.Staff.Documents.GetAsync(c.Submitted.Id)).Data<DocumentDto>();

        Assert.Equal("Customer pickup before goods-in", seenByClerk.NegativeStockOverrideReason);
        Assert.Equal("Customer pickup before goods-in", seenByStaff.NegativeStockOverrideReason);
    }

    [Fact]
    public async Task Posting_needs_no_request_body_and_ignores_a_reason_sent_by_an_old_client()
    {
        await using var c = await ShortIssueAsync();
        (await c.S.Supervisor.Documents.ApproveWithReasonAsync(c.Submitted.Id, "Reason from the approver")).ShouldBeOk();

        var posted = (await c.S.Admin.SendAsync(HttpMethod.Post, $"/api/documents/{c.Submitted.Id}/post", new { negativeStockOverrideReason = "Reason from the poster" }, r => r.Headers.TryAddWithoutValidation("If-Match", "*"))).Data<DocumentDto>();

        Assert.Equal(DocumentStatus.Posted, posted.Status);
        Assert.Equal("Reason from the approver", posted.NegativeStockOverrideReason);
        Assert.Equal(-3, await c.S.OnHandAsync(c.Product.Id, Warehouse));
    }

    [Fact]
    public async Task An_admin_can_approve_and_post_an_override()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await using var _ = await SettingsOverride.ApplyAsync(s.Admin, allowNegativeStock: true);
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 2)));

        (await s.Admin.Documents.ApproveWithReasonAsync(submitted.Id, "Opening count pending")).ShouldBeOk();
        var posted = (await s.Admin.Documents.PostAsync(submitted.Id)).Data<DocumentDto>();

        Assert.Equal(DocumentStatus.Posted, posted.Status);
        Assert.Equal("Opening count pending", posted.NegativeStockOverrideReason);
        Assert.Equal(-2, await s.OnHandAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task The_submitter_cannot_approve_their_own_override_even_with_a_reason()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await using var _ = await SettingsOverride.ApplyAsync(s.Admin, allowNegativeStock: true, allowSelfApproval: false);
        var submitted = await s.SubmitAsync(s.Supervisor, await s.DraftIssueAsync(s.Supervisor, Warehouse, new Line(product.Id, 2)));

        var own = await s.Supervisor.Documents.ApproveWithReasonAsync(submitted.Id, "I will allow it myself");

        Assert.Contains("submitted", own.ShouldBeProblem(HttpStatusCode.BadRequest).Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DocumentStatus.Submitted, (await s.GetAsync(submitted.Id)).Status);
        (await s.Admin.Documents.ApproveWithReasonAsync(submitted.Id, "Opening count pending")).ShouldBeOk();
    }

    [Fact]
    public async Task No_override_reason_is_needed_when_stock_is_sufficient()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 2m));
        await using var _ = await SettingsOverride.ApplyAsync(s.Admin, allowNegativeStock: true);
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 3)));

        var posted = (await s.Staff.Documents.PostAsync(submitted.Id)).Data<DocumentDto>();

        Assert.False(submitted.RequiresOverride);
        Assert.Null(posted.NegativeStockOverrideReason);
        Assert.Equal(2, await s.OnHandAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task Stock_that_fell_short_after_submission_cannot_be_posted_because_no_reason_was_recorded()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 2m));
        await using var _ = await SettingsOverride.ApplyAsync(s.Admin, allowNegativeStock: true);
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 3)));
        await s.IssueAsync(Warehouse, new Line(product.Id, 4)); // someone else takes the stock first

        var response = await s.Supervisor.Documents.PostAsync(submitted.Id);

        Assert.Contains("reason", response.ShouldBeProblem(HttpStatusCode.BadRequest).Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await s.OnHandAsync(product.Id, Warehouse));
        Assert.Equal(DocumentStatus.Submitted, (await s.GetAsync(submitted.Id)).Status);
    }

    [Fact]
    public async Task With_the_setting_off_an_override_cannot_happen()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 2, 1m));
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 2)));
        await s.IssueAsync(Warehouse, new Line(product.Id, 2)); // someone else takes the stock first

        var response = await s.Supervisor.Documents.PostAsync(submitted.Id);

        response.ShouldBeProblem(HttpStatusCode.Conflict, "Insufficient stock");
        Assert.Equal(0, await s.OnHandAsync(product.Id, Warehouse));
    }
}

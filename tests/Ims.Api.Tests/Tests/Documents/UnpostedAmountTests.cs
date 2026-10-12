using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>PS-03: an issue that has not posted yet shows its value (at the current average cost), and an approver sees why approval was needed.</summary>
[Collection(ApiCollection.Name)]
public class UnpostedAmountTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    /// <summary>100 units at cost 2.5: an issue of 4,000 would be worth 10,000; the seeded rule fires from 5,000.</summary>
    private async Task<(StockScenario S, int ProductId)> StockAsync(decimal qty = 5000, decimal cost = 2.5m)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, qty, cost));
        return (s, product.Id);
    }

    [Fact]
    public async Task A_draft_issue_shows_its_estimated_value_in_the_document_and_in_the_list()
    {
        var (s, productId) = await StockAsync();

        var draft = await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(productId, 1000));

        Assert.Equal(2500m, draft.TotalAmount);
        Assert.True(draft.AmountIsEstimate);
        Assert.All(draft.Lines, l => Assert.Equal((null, 2.5m, true), (l.UnitCost, l.BaseUnitCost, l.CostFromAverage))); // at the average: no editable cost
        var listed = (await s.Clerk.Documents.ListAsync(search: draft.No)).Data<PagedResult<DocumentListItem>>().Items.Single();
        Assert.Equal(2500m, listed.TotalAmount);
        Assert.True(listed.AmountIsEstimate);
    }

    [Fact]
    public async Task Editing_the_draft_values_it_again()
    {
        var (s, productId) = await StockAsync();
        var draft = await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(productId, 1000));

        var request = new DocumentBuilder(Warehouse).WithPartner(fixture.Reference.CustomerId).WithLines(new Line(productId, 2000)).Build();
        var edited = (await s.Clerk.Documents.UpdateAsync(draft.Id, request)).Data<DocumentDto>();

        Assert.Equal(5000m, edited.TotalAmount);
    }

    [Fact]
    public async Task The_approver_sees_the_value_that_triggered_approval_and_which_rule_it_reached()
    {
        var (s, productId) = await StockAsync();
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(productId, 4000)));
        Assert.True(submitted.RequiresApproval);

        var seen = (await s.Supervisor.Documents.GetAsync(submitted.Id)).Data<DocumentDto>();

        Assert.Equal(10_000m, seen.TotalAmount);
        Assert.True(seen.AmountIsEstimate);
        var trigger = Assert.Single(seen.ApprovalTriggers);
        Assert.Equal(ApprovalMetric.Amount, trigger.Metric);
        Assert.Equal(5000m, trigger.Threshold);
        Assert.Equal(10_000m, trigger.Actual);
        Assert.Equal(Roles.Supervisor, trigger.RoleRequired);
        // The submit response carries it too, for the "sent for approval" message.
        Assert.Equal(10_000m, submitted.TotalAmount);
        Assert.Single(submitted.ApprovalTriggers);
    }

    [Fact]
    public async Task The_inbox_row_carries_the_value_too()
    {
        var (s, productId) = await StockAsync();
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(productId, 4000)));

        var row = (await s.Supervisor.Documents.ListAsync(status: DocumentStatus.Submitted, requiresApproval: true, search: submitted.No))
            .Data<PagedResult<DocumentListItem>>().Items.Single();

        Assert.Equal(10_000m, row.TotalAmount);
        Assert.True(row.AmountIsEstimate);
    }

    [Fact]
    public async Task A_quantity_rule_is_explained_by_its_quantity_threshold()
    {
        var (s, productId) = await StockAsync(qty: 500, cost: 1m);
        await using var _ = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Issue, null, 50, Roles.Supervisor);
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(productId, 60)));

        var trigger = Assert.Single(submitted.ApprovalTriggers);

        Assert.Equal(ApprovalMetric.Quantity, trigger.Metric);
        Assert.Equal(50m, trigger.Threshold);
        Assert.Equal(60m, trigger.Actual);
    }

    [Fact]
    public async Task A_document_that_needs_no_approval_lists_no_triggers()
    {
        var (s, productId) = await StockAsync();

        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(productId, 10)));

        Assert.False(submitted.RequiresApproval);
        Assert.Empty(submitted.ApprovalTriggers);
    }

    [Fact]
    public async Task Posting_books_the_real_cost_and_the_amount_stops_being_an_estimate()
    {
        var (s, productId) = await StockAsync();
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(productId, 1000)));

        var posted = (await s.Admin.Documents.PostAsync(submitted.Id)).Data<DocumentDto>();

        Assert.Equal(2500m, posted.TotalAmount);
        Assert.False(posted.AmountIsEstimate);
        Assert.Empty(posted.ApprovalTriggers);
    }

    [Fact]
    public async Task A_receipt_keeps_its_entered_cost_and_is_never_an_estimate()
    {
        var (s, productId) = await StockAsync();

        var draft = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(productId, 10, 3m));

        Assert.Equal(30m, draft.TotalAmount);
        Assert.False(draft.AmountIsEstimate);
    }
}

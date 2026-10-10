using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>OUT-02: an issue that asks for more than is on hand is blocked, and the stock never goes below zero.</summary>
[Collection(ApiCollection.Name)]
public class InsufficientStockTests(ApiFixture fixture)
{
    private int Warehouse => fixture.Reference.DefaultWarehouseId;

    [Fact]
    public async Task Submitting_an_issue_for_more_than_is_on_hand_is_409_with_the_numbers()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 2m));
        var draft = await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 10));

        var response = await s.Clerk.Documents.SubmitAsync(draft.Id);

        var problem = response.ShouldBeProblem(HttpStatusCode.Conflict, "Insufficient stock");
        Assert.Equal(product.Id, problem.ProductId);
        Assert.Equal(5m, problem.Available);
        Assert.Equal(10m, problem.Requested);
        Assert.Equal(DocumentStatus.Draft, (await s.GetAsync(draft.Id)).Status);
        Assert.Equal(5, await s.OnHandAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task Lines_of_the_same_product_are_added_together_before_checking()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 1m));
        var draft = await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 6), new Line(product.Id, 6));

        (await s.Clerk.Documents.SubmitAsync(draft.Id)).ShouldBeProblem(HttpStatusCode.Conflict, "Insufficient stock");
    }

    [Fact]
    public async Task Issuing_exactly_what_is_on_hand_is_allowed_and_leaves_zero()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 8, 1m));

        await s.IssueAsync(Warehouse, new Line(product.Id, 8));

        Assert.Equal(0, await s.OnHandAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task A_product_that_was_never_stocked_in_the_warehouse_has_zero_available()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 1));

        var problem = (await s.Clerk.Documents.SubmitAsync(draft.Id)).ShouldBeProblem(HttpStatusCode.Conflict, "Insufficient stock");

        Assert.Equal(0m, problem.Available);
    }

    [Fact]
    public async Task Stock_in_another_warehouse_does_not_count()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var other = await s.NewWarehouseAsync();
        await s.ReceiveAsync(other.Id, new Line(product.Id, 100, 1m));
        var draft = await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 1));

        (await s.Clerk.Documents.SubmitAsync(draft.Id)).ShouldBeProblem(HttpStatusCode.Conflict, "Insufficient stock");
    }

    [Fact]
    public async Task Stock_taken_by_another_issue_between_submit_and_post_blocks_the_second_post_and_nothing_goes_negative()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 1m));
        var first = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 8)));
        var second = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 8)));

        (await s.Staff.Documents.PostAsync(first.Id)).ShouldBeOk();
        var blocked = await s.Staff.Documents.PostAsync(second.Id);

        blocked.ShouldBeProblem(HttpStatusCode.Conflict, "Insufficient stock");
        Assert.Equal(2, await s.OnHandAsync(product.Id, Warehouse));
        Assert.Equal(DocumentStatus.Submitted, (await s.GetAsync(second.Id)).Status);
        Assert.Single(await s.LedgerAsync(sourceDocId: first.Id));
        Assert.Empty(await s.LedgerAsync(sourceDocId: second.Id));
    }

    [Fact]
    public async Task Reversing_a_receipt_whose_stock_was_already_issued_is_refused_and_changes_nothing()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var receipt = await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 1m));
        await s.IssueAsync(Warehouse, new Line(product.Id, 4));

        var response = await s.Supervisor.Documents.ReverseAsync(receipt.Id, "Wrong supplier");

        response.ShouldBeProblem(HttpStatusCode.Conflict, "Insufficient stock");
        Assert.Equal(DocumentStatus.Posted, (await s.GetAsync(receipt.Id)).Status);
        Assert.Equal(6, await s.OnHandAsync(product.Id, Warehouse));
        Assert.Single(await s.LedgerAsync(sourceDocId: receipt.Id));
    }
}

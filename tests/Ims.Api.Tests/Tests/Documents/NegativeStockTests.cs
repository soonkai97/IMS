using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>BR-03: stock may go below zero only when the setting allows it, a supervisor (or higher) posts, and a reason is given.</summary>
[Collection(ApiCollection.Name)]
public class NegativeStockTests(ApiFixture fixture)
{
    private int Warehouse => fixture.Reference.DefaultWarehouseId;

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
    public async Task Warehouse_staff_cannot_post_below_zero_even_when_the_setting_is_on()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 2m));
        await using var _ = await SettingsOverride.ApplyAsync(s.Admin, allowNegativeStock: true);
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 8)));

        var response = await s.Staff.Documents.PostAsync(submitted.Id, "I need it");

        Assert.Contains("supervisor", response.ShouldBeProblem(HttpStatusCode.BadRequest).Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, await s.OnHandAsync(product.Id, Warehouse));
        Assert.Equal(DocumentStatus.Submitted, (await s.GetAsync(submitted.Id)).Status);
    }

    [Fact]
    public async Task A_supervisor_needs_a_reason_and_then_posts_below_zero_with_the_override_recorded()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 2m));
        await using var _ = await SettingsOverride.ApplyAsync(s.Admin, allowNegativeStock: true);
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 8)));

        var withoutReason = await s.Supervisor.Documents.PostAsync(submitted.Id);
        var blankReason = await s.Supervisor.Documents.PostAsync(submitted.Id, "   ");
        Assert.Contains("reason", withoutReason.ShouldBeProblem(HttpStatusCode.BadRequest).Detail, StringComparison.OrdinalIgnoreCase);
        blankReason.ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal(5, await s.OnHandAsync(product.Id, Warehouse));

        var posted = (await s.Supervisor.Documents.PostAsync(submitted.Id, "Customer pickup before goods-in")).Data<DocumentDto>();

        Assert.Equal(DocumentStatus.Posted, posted.Status);
        Assert.Equal("Customer pickup before goods-in", posted.NegativeStockOverrideReason);
        Assert.Equal(-3, await s.OnHandAsync(product.Id, Warehouse));
        Assert.Equal(-3m, await s.LedgerSumAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task An_admin_can_also_override()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await using var _ = await SettingsOverride.ApplyAsync(s.Admin, allowNegativeStock: true);
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 2)));

        var posted = (await s.Admin.Documents.PostAsync(submitted.Id, "Opening count pending")).Data<DocumentDto>();

        Assert.Equal(DocumentStatus.Posted, posted.Status);
        Assert.Equal(-2, await s.OnHandAsync(product.Id, Warehouse));
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

        Assert.Null(posted.NegativeStockOverrideReason);
        Assert.Equal(2, await s.OnHandAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task With_the_setting_off_an_override_reason_does_not_help()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 2, 1m));
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 2)));
        await s.IssueAsync(Warehouse, new Line(product.Id, 2)); // someone else takes the stock first

        var response = await s.Supervisor.Documents.PostAsync(submitted.Id, "please");

        response.ShouldBeProblem(HttpStatusCode.Conflict, "Insufficient stock");
        Assert.Equal(0, await s.OnHandAsync(product.Id, Warehouse));
    }
}

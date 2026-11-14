using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Sales;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>
/// The two sums that say what was already delivered against a sales order and what was already returned of a document (<c>EfDeliveredQuantityReader</c>, <c>EfReturnedQuantityReader</c>) must start from the seek on the
/// filtered document index. Compiled for the key that arrives first, the optimizer picked a scan of every document line for a key newer than the statistics of that sparse index: about 30,000 page reads and 0.3 s per call
/// at 1,000,000 documents (docs/dba/README.md section 20). Both statements therefore ask for one plan from the average statistics (<c>DISABLE_PARAMETER_SNIFFING</c>), like the documents list. Plan quality cannot be asserted
/// in a test without being flaky (the plan depends on the statistics of the moment); what is asserted is that each statement carries the hint, that it is still ONE command, and that the sums are the same as before
/// (partial deliveries and returns of one original add up, a Draft or another order does not count). The requests go through the real host, so the interceptor registration in <c>AddInfrastructure</c> is what is tested.
/// </summary>
[Collection(ApiCollection.Name)]
public class QuantityReaderPlanTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Hint = "OPTION (USE HINT('DISABLE_PARAMETER_SNIFFING'))";

    private StockScenario _s = null!;

    public async Task InitializeAsync()
    {
        await Sales.SalesScenario.ResetAsync(fixture);
        foreach (var module in new[] { "purchasing", "sales", "returns" })
            await fixture.Database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (@k, N'true')", ("@k", $"Module.{module}"));
        _s = await StockScenario.CreateAsync(fixture);
    }

    public Task DisposeAsync() => Sales.SalesScenario.ResetAsync(fixture);

    private static DocumentSaveRequest Resave(DocumentDto d, decimal qty) =>
        new(d.WarehouseId, d.PartnerId, d.Purpose, d.Remark, d.Lines.Select(l => new DocumentLineInput(l.ProductId, qty, l.UomId, null, l.Remark, ReturnOfLineNo: l.ReturnOfLineNo)).ToList());

    [Fact]
    public async Task The_delivered_sum_carries_the_hint_is_one_command_and_adds_the_posted_deliveries_of_the_order_per_line()
    {
        var a = (await _s.NewProductAsync()).Id;
        var b = (await _s.NewProductAsync()).Id;
        async Task<SalesOrderDto> OrderAsync()
        {
            var draft = (await _s.Clerk.SalesOrders.CreateAsync(new SalesOrderBuilder(_s.Reference.DefaultWarehouseId, fixture.Reference.CustomerId).WithLine(a, 10m, 2.5m).WithLine(b, 5m, 4m).Build())).Data<SalesOrderDto>();
            return (await _s.Clerk.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<SalesOrderDto>();
        }
        Task SetAsync(DocumentDto delivery, DocumentStatus status, decimal firstLineQty) =>
            fixture.Database.ExecuteAsync("UPDATE StockDocument SET Status = @s WHERE No = @no; UPDATE StockDocumentLine SET Qty = @q, BaseQty = @q WHERE DocumentId = (SELECT Id FROM StockDocument WHERE No = @no) AND SalesOrderLineNo = 1",
                ("@s", (int)status), ("@q", firstLineQty), ("@no", delivery.No));

        var order = await OrderAsync();
        var other = await OrderAsync();
        var first = (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).Data<DocumentDto>();
        await SetAsync(first, DocumentStatus.Posted, 6m);                         // 6 of line 1 and all 5 of line 2 are out
        var second = (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).Data<DocumentDto>();
        await SetAsync(second, DocumentStatus.Posted, 3m);                        // 3 more of line 1
        var foreign = (await _s.Clerk.SalesOrders.CreateDeliveryAsync(other.Id)).Data<DocumentDto>();
        await SetAsync(foreign, DocumentStatus.Posted, 7m);                       // another order: not counted here
        (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).ShouldBeOk();  // a Draft delivery (1 of line 1 is left): not counted

        var log = new CommandTextLog();
        using var host = fixture.CreateHost(b => b.ConfigureServices(services => services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(log))));
        var admin = ApiFixture.CreateClient(host);
        admin.AccessToken = (await fixture.AsAsync(Roles.Admin)).AccessToken;

        (await admin.SalesOrders.GetAsync(order.Id)).ShouldBeOk();                // the first request of a host does work once
        log.Clear();
        var read = (await admin.SalesOrders.GetAsync(order.Id)).Data<SalesOrderDto>();

        var sum = Assert.Single(log.Containing("SalesOrderLineNo"), t => t.Contains("SUM(", StringComparison.Ordinal));
        Assert.EndsWith(Hint, sum, StringComparison.Ordinal);
        Assert.DoesNotContain("RECOMPILE", sum, StringComparison.Ordinal);
        Assert.Equal((9m, 1m, 9m, 1m), (read.Lines[0].DeliveredQty, read.Lines[0].RemainingQty, read.Lines[0].DeliveredBaseQty, read.Lines[0].RemainingBaseQty));
        Assert.Equal((5m, 0m), (read.Lines[1].DeliveredQty, read.Lines[1].RemainingQty));
        var otherRead = (await admin.SalesOrders.GetAsync(other.Id)).Data<SalesOrderDto>();
        Assert.Equal((7m, 3m), (otherRead.Lines[0].DeliveredQty, otherRead.Lines[0].RemainingQty));
    }

    [Fact]
    public async Task The_returned_sum_carries_the_hint_is_one_command_and_adds_the_posted_returns_of_the_original_per_line_a_reversed_one_counts_as_nothing()
    {
        var warehouse = _s.Reference.DefaultWarehouseId;
        var product = await _s.NewProductAsync();
        await _s.ReceiveAsync(warehouse, new Line(product.Id, 20m, 3m));
        var sale = await _s.IssueAsync(warehouse, new Line(product.Id, 8m));
        var otherSale = await _s.IssueAsync(warehouse, new Line(product.Id, 2m));

        async Task<DocumentDto> PostedReturnAsync(DocumentDto original, decimal qty)
        {
            var draft = (await _s.Supervisor.Returns.CreateAsync(original.Id)).Data<DocumentDto>();
            var partial = (await _s.Supervisor.Documents.UpdateAsync(draft.Id, Resave(draft, qty))).Data<DocumentDto>();
            var submitted = (await _s.Supervisor.Documents.SubmitAsync(partial.Id)).Data<DocumentDto>();
            var approved = (await _s.Admin.Documents.ApproveAsync(submitted.Id)).Data<DocumentDto>();
            return (await _s.Admin.Documents.PostAsync(approved.Id)).Data<DocumentDto>();
        }

        await PostedReturnAsync(sale, 3m);
        await PostedReturnAsync(sale, 2m);
        var reversed = await PostedReturnAsync(sale, 1m);
        (await _s.Admin.Documents.ReverseAsync(reversed.Id, "returned in error")).ShouldBeOk();
        await PostedReturnAsync(otherSale, 2m);                                   // another original: not counted here
        (await _s.Supervisor.Returns.CreateAsync(sale.Id)).ShouldBeOk();          // a Draft return: not counted

        var log = new CommandTextLog();
        using var host = fixture.CreateHost(b => b.ConfigureServices(services => services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(log))));
        var supervisor = ApiFixture.CreateClient(host);
        supervisor.AccessToken = (await fixture.AsAsync(Roles.Supervisor)).AccessToken;

        (await supervisor.Documents.GetAsync(sale.Id)).ShouldBeOk();              // the first request of a host does work once
        log.Clear();
        var read = (await supervisor.Documents.GetAsync(sale.Id)).Data<DocumentDto>();

        var sum = Assert.Single(log.Containing("ReturnOfLineNo"), t => t.Contains("SUM(", StringComparison.Ordinal));
        Assert.EndsWith(Hint, sum, StringComparison.Ordinal);
        Assert.DoesNotContain("RECOMPILE", sum, StringComparison.Ordinal);
        Assert.Equal((5m, 3m), (read.Lines[0].ReturnedQty, read.Lines[0].ReturnableQty));
    }
}

using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Sales;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// PL3.6 acceptance 1 to 3 and 5: what the order says about its deliveries (delivered and remaining per line, the list), the cancel it refuses while a delivery is open, the <c>deliver</c> action and the order
/// number on a delivery. Posting is PL3.7, so a Posted delivery is set the way a post leaves it (status and quantities) straight in the table.
/// </summary>
[Collection(ApiCollection.Name)]
public class SalesOrderDeliveryReadTests(ApiFixture fixture) : IAsyncLifetime
{
    private SalesScenario _s = null!;

    public async Task InitializeAsync() => _s = await SalesScenario.CreateAsync(fixture);

    public Task DisposeAsync() => SalesScenario.ResetAsync(fixture);

    private ApiClient Forged(IEnumerable<string> permissions) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    private async Task<(SalesOrderDto Order, DocumentDto Delivery)> DeliveryAsync()
    {
        var a = (await _s.NewProductAsync()).Id;
        var b = (await _s.NewProductAsync()).Id;
        var draft = (await _s.Clerk.SalesOrders.CreateAsync(new SalesOrderBuilder(_s.Warehouse, fixture.Reference.CustomerId).WithLine(a, 10m, 2.5m).WithLine(b, 5m, 4m).Build())).Data<SalesOrderDto>();
        var order = (await _s.Clerk.SalesOrders.SubmitAsync(draft.Id, draft.ConcurrencyToken)).Data<SalesOrderDto>();
        return (order, (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).Data<DocumentDto>());
    }

    /// <summary>Sets a delivery to a state and gives its first line a quantity, the way posting will leave it.</summary>
    private async Task SetAsync(DocumentDto delivery, DocumentStatus status, decimal? firstLineQty = null)
    {
        await fixture.Database.ExecuteAsync("UPDATE StockDocument SET Status = @s WHERE No = @no", ("@s", (int)status), ("@no", delivery.No));
        if (firstLineQty is { } q)
            await fixture.Database.ExecuteAsync("UPDATE StockDocumentLine SET Qty = @q, BaseQty = @q WHERE DocumentId = (SELECT Id FROM StockDocument WHERE No = @no) AND SalesOrderLineNo = 1", ("@q", q), ("@no", delivery.No));
    }

    // ----- acceptance 1: delivered and remaining -----
    [Fact]
    public async Task A_posted_delivery_shows_as_delivered_and_remaining_per_line_and_a_reversal_gives_it_back()
    {
        var (order, delivery) = await DeliveryAsync();
        Assert.All((await _s.ReadAsync(order.Id)).Lines, l => Assert.Equal((0m, l.Qty), (l.DeliveredQty, l.RemainingQty)));

        await SetAsync(delivery, DocumentStatus.Posted, firstLineQty: 6m);
        var posted = (await _s.ReadAsync(order.Id)).Lines;
        Assert.Equal((6m, 4m, 6m, 4m), (posted[0].DeliveredQty, posted[0].RemainingQty, posted[0].DeliveredBaseQty, posted[0].RemainingBaseQty));
        Assert.Equal((5m, 0m), (posted[1].DeliveredQty, posted[1].RemainingQty)); // line 2 was delivered in full by the same delivery

        await SetAsync(delivery, DocumentStatus.Reversed);
        var reversed = (await _s.ReadAsync(order.Id)).Lines;
        Assert.All(reversed, l => Assert.Equal((0m, l.Qty), (l.DeliveredQty, l.RemainingQty)));
    }

    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Submitted)]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Cancelled)]
    public async Task A_delivery_that_is_not_posted_counts_as_nothing(DocumentStatus status)
    {
        var (order, delivery) = await DeliveryAsync();
        await SetAsync(delivery, status, firstLineQty: 7m);

        var lines = (await _s.ReadAsync(order.Id)).Lines;

        Assert.All(lines, l => Assert.Equal((0m, l.Qty), (l.DeliveredQty, l.RemainingQty)));
    }

    [Fact]
    public async Task A_draft_and_a_submitted_order_say_everything_is_still_to_deliver_and_list_no_deliveries()
    {
        var draft = await _s.DraftWorthAsync(10m);
        var submitted = await _s.SubmittedWorthAsync(6000m);

        foreach (var read in new[] { await _s.ReadAsync(draft.Id), await _s.ReadAsync(submitted.Id) })
        {
            Assert.All(read.Lines, l => Assert.Equal((0m, l.Qty), (l.DeliveredQty, l.RemainingQty)));
            Assert.Empty(read.Deliveries);
        }
    }

    [Fact]
    public async Task The_order_lists_its_deliveries_oldest_first_in_every_state_with_number_and_status_and_no_money()
    {
        var (order, first) = await DeliveryAsync();
        var second = (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).Data<DocumentDto>();
        await SetAsync(second, DocumentStatus.Cancelled);

        var read = await _s.Staff.SalesOrders.GetAsync(order.Id);

        var listed = read.Data<SalesOrderDto>().Deliveries;
        Assert.Equal([(first.No, DocumentStatus.Draft), (second.No, DocumentStatus.Cancelled)], listed.Select(d => (d.No, d.Status)));
        Assert.Equal([first.Id, second.Id], listed.Select(d => d.Id));
        Assert.DoesNotContain("amount", read.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("price", read.Body, StringComparison.OrdinalIgnoreCase);
    }

    // ----- acceptance 2: cancel -----
    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Submitted)]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Posted)]
    public async Task An_order_with_a_delivery_open_or_posted_cannot_be_cancelled_409_naming_the_delivery(DocumentStatus status)
    {
        var (order, delivery) = await DeliveryAsync();
        await SetAsync(delivery, status);

        var problem = (await _s.Admin.SalesOrders.CancelAsync(order.Id, "Customer withdrew the order", order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(Messages.SalesOrderHasDeliveries(order.No, delivery.No, status, 1), problem.Detail);
        Assert.Equal(SalesOrderStatus.Approved, (await _s.ReadAsync(order.Id)).Status);
    }

    [Theory]
    [InlineData(DocumentStatus.Cancelled)]
    [InlineData(DocumentStatus.Reversed)]
    public async Task A_cancelled_or_reversed_delivery_does_not_block_the_cancel(DocumentStatus status)
    {
        var (order, delivery) = await DeliveryAsync();
        await SetAsync(delivery, status);

        var cancelled = (await _s.Admin.SalesOrders.CancelAsync(order.Id, "Customer withdrew the order", order.ConcurrencyToken)).Data<SalesOrderDto>();

        Assert.Equal(SalesOrderStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task The_refusal_names_how_many_more_deliveries_are_in_the_way()
    {
        var (order, first) = await DeliveryAsync();
        await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id);
        await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id);

        var problem = (await _s.Admin.SalesOrders.CancelAsync(order.Id, "Customer withdrew the order", order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Contains(first.No, problem.Detail);
        Assert.Contains("and 2 more", problem.Detail);
    }

    // ----- acceptance 3: the deliver action -----
    [Fact]
    public async Task Deliver_is_offered_on_an_approved_order_with_something_left_to_a_holder_of_document_create_and_so_read_and_the_endpoint_agrees()
    {
        var (order, _) = await DeliveryAsync();
        var both = Forged([Permissions.DocumentCreate, Permissions.SoRead, Permissions.StockRead, Permissions.MasterDataRead]);
        var onlyCreate = Forged([Permissions.DocumentCreate, Permissions.SoManage]);   // cannot read the order's figures: so.manage requires so.read in the catalog, but a forged token does not
        var onlyRead = Forged([Permissions.SoRead]);

        Assert.Contains(DocumentActions.Deliver, (await both.SalesOrders.GetAsync(order.Id)).Data<SalesOrderDto>().AllowedActions);
        Assert.DoesNotContain(DocumentActions.Deliver, (await onlyRead.SalesOrders.GetAsync(order.Id)).Data<SalesOrderDto>().AllowedActions);
        (await both.SalesOrders.CreateDeliveryAsync(order.Id)).ShouldBeOk();
        (await onlyRead.SalesOrders.CreateDeliveryAsync(order.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await onlyCreate.SalesOrders.CreateDeliveryAsync(order.Id)).ShouldBeProblem(HttpStatusCode.Forbidden); // no so.read
        Assert.Contains(DocumentActions.Deliver, (await _s.Staff.SalesOrders.GetAsync(order.Id)).Data<SalesOrderDto>().AllowedActions); // the Warehouse staff deliver
    }

    [Fact]
    public async Task Deliver_is_not_offered_when_the_order_is_not_approved_or_nothing_is_left()
    {
        var draft = await _s.DraftWorthAsync(10m);
        var submitted = await _s.SubmittedWorthAsync(6000m);
        var (order, delivery) = await DeliveryAsync();

        Assert.DoesNotContain(DocumentActions.Deliver, (await _s.ReadAsync(draft.Id, _s.Clerk)).AllowedActions);
        Assert.DoesNotContain(DocumentActions.Deliver, (await _s.ReadAsync(submitted.Id, _s.Clerk)).AllowedActions);
        Assert.Contains(DocumentActions.Deliver, (await _s.ReadAsync(order.Id, _s.Clerk)).AllowedActions);

        await SetAsync(delivery, DocumentStatus.Posted, firstLineQty: 10m); // both lines delivered in full
        var done = await _s.ReadAsync(order.Id, _s.Clerk);
        Assert.DoesNotContain(DocumentActions.Deliver, done.AllowedActions);
        (await _s.Clerk.SalesOrders.CreateDeliveryAsync(order.Id)).ShouldBeProblem(HttpStatusCode.Conflict); // and the endpoint agrees
    }

    // ----- acceptance 5: the order number on a delivery -----
    [Fact]
    public async Task The_delivery_shows_its_order_number_to_a_holder_of_so_read_only_while_sales_is_on()
    {
        var (order, delivery) = await DeliveryAsync();

        Assert.Equal(order.No, (await _s.Staff.Documents.GetAsync(delivery.Id)).Data<DocumentDto>().SalesOrder!.No);
        Assert.Null((await Forged([Permissions.DocumentCreate, Permissions.StockRead, Permissions.MasterDataRead]).Documents.GetAsync(delivery.Id)).Data<DocumentDto>().SalesOrder);
        await _s.TurnOffAsync();
        Assert.Null((await _s.Staff.Documents.GetAsync(delivery.Id)).Data<DocumentDto>().SalesOrder);
    }
}

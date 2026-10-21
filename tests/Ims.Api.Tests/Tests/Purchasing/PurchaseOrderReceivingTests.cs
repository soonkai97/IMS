using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Purchasing;
using Ims.Domain;
using static Ims.Api.Tests.Tests.Purchasing.PurchasingScenario;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// PL2.4 acceptance 5 to 7: what an order says about its receipts. Received and remaining are COMPUTED on every read from the Posted receipts (a Draft, Submitted, Approved, Cancelled or Reversed
/// receipt counts as nothing, so a reversal frees its quantity with no code), the order lists its receipts, an order with a receipt open or posted cannot be cancelled, and creating a receipt
/// races a cancel of the order safely (the order's lock).
/// </summary>
[Collection(ApiCollection.Name)]
public class PurchaseOrderReceivingTests(ApiFixture fixture) : IAsyncLifetime
{
    private PurchasingScenario _p = null!;
    private ProductDto _a = null!;

    public async Task InitializeAsync()
    {
        _p = await PurchasingScenario.CreateAsync(fixture);
        _a = await _p.NewProductAsync();
    }

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    private async Task<DocumentDto> PostedAsync(PurchaseOrderDto order, decimal qty, int lineNo = 1)
    {
        var receipt = await _p.ReceiptFromAsync(order);
        var line = receipt.Lines.Single(l => l.PurchaseOrderLineNo == lineNo);
        var saved = (await _p.Clerk.Documents.UpdateAsync(receipt.Id, Save(receipt, LineFor(line.ProductId, qty, lineNo, uomId: line.UomId)), receipt.ConcurrencyToken)).Data<DocumentDto>();
        return await _p.PostAsync(saved, _p.Clerk);
    }

    private static (decimal Received, decimal Remaining, decimal ReceivedBase, decimal RemainingBase) Line(PurchaseOrderDto order, int lineNo = 1)
    {
        var l = order.Lines.Single(x => x.LineNo == lineNo);
        return (l.ReceivedQty, l.RemainingQty, l.ReceivedBaseQty, l.RemainingBaseQty);
    }

    [Fact]
    public async Task An_approved_order_with_nothing_received_shows_everything_remaining_and_no_receipts()
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null));

        var read = await _p.ReadAsync(order.Id);

        Assert.Equal((0m, 10m, 0m, 10m), Line(read));
        Assert.Empty(read.Receipts);
    }

    [Fact]
    public async Task Six_of_ten_posted_shows_received_six_and_remaining_four_and_reversing_it_shows_none_and_ten()
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null));

        var posted = await PostedAsync(order, 6m);

        var read = await _p.ReadAsync(order.Id);
        Assert.Equal((6m, 4m, 6m, 4m), Line(read));
        var listed = Assert.Single(read.Receipts);
        Assert.Equal((posted.Id, posted.No, DocumentStatus.Posted), (listed.Id, listed.No, listed.Status));
        Assert.NotNull(listed.PostedAt);

        (await _p.Admin.Documents.ReverseAsync(posted.Id, "Entered twice")).ShouldBeOk();

        read = await _p.ReadAsync(order.Id);
        Assert.Equal((0m, 10m, 0m, 10m), Line(read)); // the quantity is free again, the receipt is still listed
        Assert.Equal(DocumentStatus.Reversed, Assert.Single(read.Receipts).Status);
    }

    [Fact]
    public async Task A_receipt_that_is_a_draft_submitted_approved_or_cancelled_counts_as_nothing_but_is_listed()
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null));
        var draft = await _p.ReceiptFromAsync(order);
        var submitted = await _p.ReceiptFromAsync(order);
        (await _p.Clerk.Documents.SubmitAsync(submitted.Id, submitted.ConcurrencyToken)).ShouldBeOk();
        var cancelled = await _p.ReceiptFromAsync(order);
        (await _p.Clerk.Documents.CancelAsync(cancelled.Id, "Not needed", cancelled.ConcurrencyToken)).ShouldBeOk();

        var read = await _p.ReadAsync(order.Id);

        Assert.Equal((0m, 10m, 0m, 10m), Line(read));
        Assert.Equal([DocumentStatus.Draft, DocumentStatus.Submitted, DocumentStatus.Cancelled], read.Receipts.Select(r => r.Status));
        Assert.Equal([draft.No, submitted.No, cancelled.No], read.Receipts.Select(r => r.No)); // oldest first
    }

    [Fact]
    public async Task Received_and_remaining_are_per_order_line_and_in_base_units_for_a_carton_line()
    {
        var carton = (await _p.Admin.Units.CreateAsync(Unique.Code("CT"), "Carton")).Data<UomDto>();
        var c = await _p.NewProductAsync(customize: b => b.WithConversion(carton.Id, 12m));
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null), (_a.Id, 5m, 3m, null), (c.Id, 4m, 100m, carton.Id));

        await PostedAsync(order, 3m, lineNo: 3); // 3 of 4 cartons

        var read = await _p.ReadAsync(order.Id);
        Assert.Equal((0m, 10m, 0m, 10m), Line(read, 1)); // the other lines of the same product A are untouched
        Assert.Equal((0m, 5m, 0m, 5m), Line(read, 2));
        Assert.Equal((3m, 1m, 36m, 12m), Line(read, 3)); // cartons, and the same in pieces
    }

    [Fact]
    public async Task Warehouse_staff_see_the_quantities_and_the_receipts_and_no_price()
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2.5m, null));
        await PostedAsync(order, 4m);

        var response = await _p.Staff.PurchaseOrders.GetAsync(order.Id);
        var read = response.Data<PurchaseOrderDto>();

        Assert.Equal((4m, 6m, 4m, 6m), Line(read));
        Assert.Single(read.Receipts);
        Assert.Null(read.Lines.Single().UnitPrice);
        Assert.Null(read.TotalAmount);
        Assert.DoesNotContain("2.5", response.Body); // no money anywhere in the answer
    }

    [Fact]
    public async Task A_draft_or_submitted_order_has_no_receipts_and_reads_with_nothing_received()
    {
        var draft = await _p.CreateAsync(_p.Warehouse, _a.Id, _p.Clerk);

        var read = await _p.ReadAsync(draft.Id);

        Assert.Equal((0m, 10m, 0m, 10m), Line(read));
        Assert.Empty(read.Receipts);
    }

    [Fact]
    public async Task The_receipts_of_an_order_in_another_warehouse_are_not_visible_to_someone_who_cannot_see_the_order()
    {
        var other = await _p.NewWarehouseAsync();
        var order = (await _p.Admin.PurchaseOrders.CreateAsync(_p.Request(other.Id, _a.Id).Build())).Data<PurchaseOrderDto>();
        order = (await _p.Admin.PurchaseOrders.SubmitAsync(order.Id, order.ConcurrencyToken)).Data<PurchaseOrderDto>();
        await _p.Admin.PurchaseOrders.CreateReceiptAsync(order.Id);

        (await _p.Clerk.PurchaseOrders.GetAsync(order.Id)).ShouldBe(HttpStatusCode.NotFound);
        Assert.Single((await _p.Admin.PurchaseOrders.GetAsync(order.Id)).Data<PurchaseOrderDto>().Receipts);
    }

    // ----- cancel -----
    [Theory]
    [InlineData("draft")]
    [InlineData("submitted")]
    [InlineData("approved")]
    [InlineData("posted")]
    public async Task An_order_with_a_receipt_open_or_posted_cannot_be_cancelled_and_the_answer_names_the_receipt(string state)
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null));
        var receipt = await _p.ReceiptFromAsync(order);
        // a rule makes the receipt wait for a reviewer, so it can be Approved without being posted
        await using var rule = state == "approved" ? await ApprovalRuleScope.AddAsync(_p.Admin, DocumentType.Receipt, null, 1m, Roles.Supervisor) : null;
        if (state is "submitted" or "approved") receipt = (await _p.Clerk.Documents.SubmitAsync(receipt.Id, receipt.ConcurrencyToken)).Data<DocumentDto>();
        if (state == "approved") receipt = (await _p.Supervisor.Documents.ApproveAsync(receipt.Id, receipt.ConcurrencyToken)).Data<DocumentDto>();
        if (state == "posted") receipt = await _p.PostAsync(receipt, _p.Clerk);

        var problem = (await _p.Admin.PurchaseOrders.CancelAsync(order.Id, "Supplier cannot deliver", order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Contains(receipt.No, problem.Detail);
        Assert.Contains($"({receipt.Status})", problem.Detail); // Draft, Submitted, Approved or Posted
        Assert.Equal(PurchaseOrderStatus.Approved, (await _p.ReadAsync(order.Id)).Status);
    }

    [Fact]
    public async Task Once_the_receipt_is_cancelled_or_reversed_the_order_can_be_cancelled()
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null));
        var draft = await _p.ReceiptFromAsync(order);
        (await _p.Admin.PurchaseOrders.CancelAsync(order.Id, "Supplier cannot deliver", order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);

        (await _p.Clerk.Documents.CancelAsync(draft.Id, "Not needed", draft.ConcurrencyToken)).ShouldBeOk();
        var posted = await PostedAsync(order, 2m);
        (await _p.Admin.PurchaseOrders.CancelAsync(order.Id, "Supplier cannot deliver", order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict); // 2 are posted

        (await _p.Admin.Documents.ReverseAsync(posted.Id, "Entered twice")).ShouldBeOk();
        var cancelled = (await _p.Admin.PurchaseOrders.CancelAsync(order.Id, "Supplier cannot deliver", order.ConcurrencyToken)).Data<PurchaseOrderDto>();

        Assert.Equal(PurchaseOrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(2, cancelled.Receipts.Count); // history stays on the order
        // and a cancelled order cannot be received
        (await _p.Clerk.PurchaseOrders.CreateReceiptAsync(order.Id)).ShouldBeProblem(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Creating_a_receipt_and_cancelling_the_order_at_the_same_moment_never_leave_a_receipt_on_a_cancelled_order()
    {
        for (var round = 0; round < 8; round++)
        {
            var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null));

            var results = await Concurrently.RunAsync<ApiResponse>(
            [
                () => _p.Clerk.PurchaseOrders.CreateReceiptAsync(order.Id),
                () => _p.Admin.PurchaseOrders.CancelAsync(order.Id, "Supplier cannot deliver", order.ConcurrencyToken)
            ]);
            var (created, cancelled) = (results[0], results[1]);

            var read = await _p.ReadAsync(order.Id);
            Assert.True(created.IsSuccess ^ cancelled.IsSuccess, $"round {round}: exactly one wins (receipt {created.StatusCode}, cancel {cancelled.StatusCode})");
            if (created.IsSuccess)
            {
                cancelled.ShouldBe(HttpStatusCode.Conflict);
                Assert.Equal(PurchaseOrderStatus.Approved, read.Status);
                Assert.Single(read.Receipts);
            }
            else
            {
                created.ShouldBe(HttpStatusCode.Conflict);
                Assert.Equal(PurchaseOrderStatus.Cancelled, read.Status);
                Assert.Empty(read.Receipts);
            }
        }
    }

    [Fact]
    public async Task Receipts_made_at_the_same_moment_for_one_order_all_succeed_with_distinct_numbers_because_a_draft_reserves_nothing()
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null));

        var results = await Concurrently.RunAsync<ApiResponse>(Enumerable.Range(0, 5).Select<int, Func<Task<ApiResponse>>>(_ => () => _p.Clerk.PurchaseOrders.CreateReceiptAsync(order.Id)));

        var numbers = results.Select(r => r.Data<DocumentDto>().No).ToList();
        Assert.Equal(5, numbers.Distinct().Count());
        var read = await _p.ReadAsync(order.Id);
        Assert.Equal(5, read.Receipts.Count);
        Assert.Equal((0m, 10m, 0m, 10m), Line(read)); // nothing posted: the posting check (PL2.5) is the one that decides who gets the quantity
    }

    [Fact]
    public async Task The_module_still_cannot_be_switched_off_over_an_order_that_has_receipts_and_the_count_is_the_orders()
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null));
        await _p.ReceiptFromAsync(order);
        await _p.ReceiptFromAsync(order);

        var refused = (await _p.Admin.Modules.SwitchAsync(ModuleCodes.Purchasing, false, "Switching off")).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Contains("1 purchase order is open", refused.Detail); // the orders, not the receipts
    }
}

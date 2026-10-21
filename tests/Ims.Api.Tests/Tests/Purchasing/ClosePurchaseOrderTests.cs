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
/// PL2.5: closing an Approved order short, with a reason, by a holder of <c>po.manage</c>, and only once something was received (with nothing received the order is cancelled instead, so cancel keeps its own
/// authority). What was received stays; a receipt still waiting must be posted or cancelled first; a closed order cannot be received against.
/// </summary>
[Collection(ApiCollection.Name)]
public class ClosePurchaseOrderTests(ApiFixture fixture) : IAsyncLifetime
{
    private PurchasingScenario _p = null!;
    private ProductDto _a = null!;

    public async Task InitializeAsync()
    {
        _p = await PurchasingScenario.CreateAsync(fixture);
        _a = await _p.NewProductAsync();
    }

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    /// <summary>An Approved order of 10 of product A with <paramref name="received"/> of them already posted (by the Clerk), so closing short is possible. Returns the order as the Clerk reads it and the posted receipt.</summary>
    private async Task<(PurchaseOrderDto Order, DocumentDto Receipt)> PartlyReceivedAsync(decimal received = 6m)
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null));
        var receipt = await _p.ReceiptFromAsync(order);
        var saved = (await _p.Clerk.Documents.UpdateAsync(receipt.Id, Save(receipt, LineFor(_a.Id, received, 1)), receipt.ConcurrencyToken)).Data<DocumentDto>();
        var posted = await _p.PostAsync(saved, _p.Clerk);
        return (await _p.ReadAsync(order.Id, _p.Clerk), posted);
    }

    [Fact]
    public async Task A_manager_closes_a_partly_received_order_short_with_a_reason_and_it_is_closed_with_the_kind_the_reason_and_an_audit_event()
    {
        var (read, _) = await PartlyReceivedAsync();
        Assert.Contains(DocumentActions.Close, read.AllowedActions);

        var response = await _p.Clerk.PurchaseOrders.CloseAsync(read.Id, "  Supplier ships no more  ", read.ConcurrencyToken);

        var closed = response.Data<PurchaseOrderDto>();
        Assert.Equal((PurchaseOrderStatus.Closed, PurchaseOrderCloseKind.Short, "Supplier ships no more"), (closed.Status, closed.CloseKind, closed.CloseReason));
        Assert.NotNull(closed.ClosedAt);
        Assert.Equal((6m, 4m), (closed.Lines.Single().ReceivedQty, closed.Lines.Single().RemainingQty)); // what came in stays; the rest will not
        Assert.Empty(closed.AllowedActions);
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = N'purchase-order.closed' AND EntityId = @no AND Actor = @who", ("@no", read.No), ("@who", TestUsers.UsernameOf(Roles.Clerk))));
        (await _p.Clerk.PurchaseOrders.CreateReceiptAsync(read.Id)).ShouldBeProblem(HttpStatusCode.Conflict); // a closed order cannot be received
        (await _p.Admin.PurchaseOrders.CancelAsync(read.Id, "Supplier cannot deliver", closed.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest); // and is not cancelled afterwards
    }

    [Fact]
    public async Task With_nothing_received_the_order_cannot_be_closed_short_and_close_is_not_offered_so_cancelling_stays_the_only_way_to_void_it()
    {
        var order = await _p.ApprovedAsync((_a.Id, 10m, 2m, null));
        var asClerk = await _p.ReadAsync(order.Id, _p.Clerk);
        Assert.DoesNotContain(DocumentActions.Close, asClerk.AllowedActions);
        Assert.DoesNotContain(DocumentActions.Close, order.AllowedActions);

        var problem = (await _p.Clerk.PurchaseOrders.CloseAsync(order.Id, "Supplier ships no more", asClerk.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(Messages.PurchaseOrderNothingReceived(order.No), problem.Detail);
        Assert.Equal(PurchaseOrderStatus.Approved, (await _p.ReadAsync(order.Id)).Status);
        Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = N'purchase-order.closed' AND EntityId = @no", ("@no", order.No)));
    }

    [Fact]
    public async Task A_receipt_that_was_posted_and_then_reversed_counts_as_nothing_received_so_close_goes_away_again()
    {
        var (order, receipt) = await PartlyReceivedAsync(3m);
        Assert.Contains(DocumentActions.Close, order.AllowedActions);

        (await _p.Admin.Documents.ReverseAsync(receipt.Id, "Entered twice")).ShouldBeOk();

        var again = await _p.ReadAsync(order.Id, _p.Clerk);
        Assert.DoesNotContain(DocumentActions.Close, again.AllowedActions);
        Assert.Equal(Messages.PurchaseOrderNothingReceived(order.No), (await _p.Clerk.PurchaseOrders.CloseAsync(order.Id, "Supplier ships no more", again.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict).Detail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  no  ")]
    public async Task A_reason_of_at_least_five_characters_is_required(string? reason)
    {
        var (order, _) = await PartlyReceivedAsync();

        (await _p.Clerk.PurchaseOrders.CloseAsync(order.Id, reason, order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(PurchaseOrderStatus.Approved, (await _p.ReadAsync(order.Id)).Status);
    }

    [Theory]
    [InlineData(DocumentStatus.Draft)]
    [InlineData(DocumentStatus.Submitted)]
    [InlineData(DocumentStatus.Approved)]
    public async Task An_order_with_a_receipt_waiting_cannot_be_closed_and_the_answer_names_the_receipt_and_after_it_is_posted_it_can(DocumentStatus waiting)
    {
        var (order, _) = await PartlyReceivedAsync(); // 6 of 10 are in
        var receipt = await _p.ReceiptFromAsync(order); // prefilled with the 4 that remain
        // a rule makes the receipt wait for a reviewer, so it can be Approved without being posted
        await using var rule = waiting == DocumentStatus.Approved ? await ApprovalRuleScope.AddAsync(_p.Admin, DocumentType.Receipt, null, 1m, Roles.Supervisor) : null;
        if (waiting is DocumentStatus.Submitted or DocumentStatus.Approved) receipt = (await _p.Clerk.Documents.SubmitAsync(receipt.Id, receipt.ConcurrencyToken)).Data<DocumentDto>();
        if (waiting == DocumentStatus.Approved) receipt = (await _p.Supervisor.Documents.ApproveAsync(receipt.Id, receipt.ConcurrencyToken)).Data<DocumentDto>();

        var problem = (await _p.Clerk.PurchaseOrders.CloseAsync(order.Id, "Supplier ships no more", order.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.Conflict);

        Assert.Equal(Messages.PurchaseOrderHasOpenReceipts(order.No, receipt.No, waiting, 1), problem.Detail);
        Assert.Equal(PurchaseOrderStatus.Approved, (await _p.ReadAsync(order.Id)).Status);
        if (waiting == DocumentStatus.Draft) receipt = await _p.Stock.SubmitAsync(_p.Clerk, receipt);
        (await _p.Admin.Documents.PostAsync(receipt.Id)).ShouldBeOk(); // posts the last 4: the order closes itself
        Assert.Equal(PurchaseOrderCloseKind.Received, (await _p.ReadAsync(order.Id)).CloseKind);
    }

    [Fact]
    public async Task Only_an_approved_order_can_be_closed_and_only_by_someone_who_manages_orders_and_the_order_must_be_seen()
    {
        var draft = await _p.CreateAsync(_p.Warehouse, _a.Id, _p.Clerk);
        (await _p.Clerk.PurchaseOrders.CloseAsync(draft.Id, "Supplier ships no more", draft.ConcurrencyToken)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var (order, _) = await PartlyReceivedAsync();

        (await _p.Staff.PurchaseOrders.CloseAsync(order.Id, "Supplier ships no more", order.ConcurrencyToken)).ShouldBe(HttpStatusCode.Forbidden); // po.read, not po.manage
        (await _p.Owner.PurchaseOrders.CloseAsync(order.Id, "Supplier ships no more", order.ConcurrencyToken)).ShouldBe(HttpStatusCode.Forbidden);
        (await _p.Clerk.PurchaseOrders.TransitionWithoutIfMatchAsync(order.Id, "close", new CloseRequest("Supplier ships no more"))).ShouldBe((HttpStatusCode)428);
        (await _p.Clerk.PurchaseOrders.CloseAsync(order.Id, "Supplier ships no more", "stale")).ShouldBe(HttpStatusCode.Conflict);
        (await _p.Clerk.PurchaseOrders.CloseAsync(Guid.NewGuid(), "Supplier ships no more", order.ConcurrencyToken)).ShouldBe(HttpStatusCode.NotFound);
        var other = await _p.NewWarehouseAsync();
        var far = (await _p.Admin.PurchaseOrders.CreateAsync(_p.Request(other.Id, _a.Id).Build())).Data<PurchaseOrderDto>();
        far = (await _p.Admin.PurchaseOrders.SubmitAsync(far.Id, far.ConcurrencyToken)).Data<PurchaseOrderDto>();
        (await _p.Clerk.PurchaseOrders.CloseAsync(far.Id, "Supplier ships no more", far.ConcurrencyToken)).ShouldBe(HttpStatusCode.NotFound); // another warehouse than the Clerk's
        (await _p.Clerk.PurchaseOrders.CloseAsync(order.Id, "Supplier ships no more", order.ConcurrencyToken)).ShouldBeOk();
    }

    [Fact]
    public async Task While_the_module_is_off_the_close_endpoint_is_a_404_and_a_closed_order_does_not_count_as_open_for_switching_the_module_off()
    {
        var (order, _) = await PartlyReceivedAsync();
        (await _p.Admin.Modules.SwitchAsync(ModuleCodes.Purchasing, false, "Switching off")).ShouldBeProblem(HttpStatusCode.Conflict); // open
        var closed = (await _p.Clerk.PurchaseOrders.CloseAsync(order.Id, "Supplier ships no more", order.ConcurrencyToken)).Data<PurchaseOrderDto>();

        (await _p.Admin.Modules.SwitchAsync(ModuleCodes.Purchasing, false, "Switching off")).ShouldBeOk(); // closed: the data is kept

        (await _p.Clerk.PurchaseOrders.CloseAsync(order.Id, "Supplier ships no more", closed.ConcurrencyToken)).ShouldBe(HttpStatusCode.NotFound);
    }
}

using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Purchasing;

/// <summary>PL2.5: an Approved order closes when the last quantity is received (kind Received, no reason) or short (kind Short, a reason of 5 or more characters). Nothing else closes.</summary>
public class PurchaseOrderCloseTests
{
    private const int Poster = 21;

    private static PurchaseOrder Order(PurchaseOrderStatus status)
    {
        var order = PurchaseOrder.Create(NewWarehouse(), NewSupplier(), null, 11, Now);
        order.AssignNumber("PO-WH1-2601-0001");
        order.ReplaceLines([PurchaseOrderLine.Create(NewProduct(), 5m, null, 2m, null)]);
        if (status == PurchaseOrderStatus.Cancelled) order.Cancel(null);
        if (status is PurchaseOrderStatus.Approved or PurchaseOrderStatus.Closed) order.Submit(11, requiresApproval: false, isLarge: false, Now);
        if (status == PurchaseOrderStatus.Closed) order.CloseReceived(Poster, Now);
        return order;
    }

    [Fact]
    public void Receiving_the_last_quantity_closes_an_approved_order_with_the_kind_received_the_poster_and_the_time()
    {
        var order = Order(PurchaseOrderStatus.Approved);

        order.CloseReceived(Poster, Now);

        Assert.Equal((PurchaseOrderStatus.Closed, PurchaseOrderCloseKind.Received, (int?)Poster, (DateTime?)Now, (string?)null), (order.Status, order.CloseKind, order.ClosedBy, order.ClosedAt, order.CloseReason));
        Assert.False(order.CanBeReceived);
    }

    [Fact]
    public void Closing_short_keeps_the_trimmed_reason_and_the_kind_short()
    {
        var order = Order(PurchaseOrderStatus.Approved);

        order.CloseShort("  Supplier ships no more  ", Poster, Now);

        Assert.Equal((PurchaseOrderStatus.Closed, PurchaseOrderCloseKind.Short, "Supplier ships no more"), (order.Status, order.CloseKind, order.CloseReason));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no")]
    public void Closing_short_needs_a_reason_of_five_characters_and_a_refusal_changes_nothing(string? reason)
    {
        var order = Order(PurchaseOrderStatus.Approved);

        Assert.Throws<DomainException>(() => order.CloseShort(reason, Poster, Now));

        Assert.Equal((PurchaseOrderStatus.Approved, (PurchaseOrderCloseKind?)null), (order.Status, order.CloseKind));
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft)]
    [InlineData(PurchaseOrderStatus.Closed)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public void Only_an_approved_order_closes_in_either_way(PurchaseOrderStatus status)
    {
        var order = Order(status);

        Assert.Equal(Messages.PurchaseOrderCannotClose(status), Assert.Throws<DomainException>(() => order.CloseShort("Supplier ships no more", Poster, Now)).Message);
        Assert.Equal(Messages.PurchaseOrderCannotClose(status), Assert.Throws<DomainException>(() => order.CloseReceived(Poster, Now)).Message);
    }
}

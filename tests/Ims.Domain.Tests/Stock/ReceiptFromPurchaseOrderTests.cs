using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>PL2.4: a receipt created from a purchase order carries the link for good, and only an Approved order can be received.</summary>
public class ReceiptFromPurchaseOrderTests
{
    private static PurchaseOrder Order(bool approved)
    {
        var order = PurchaseOrder.Create(NewWarehouse(), NewSupplier(), null, 11, Now).WithId(7);
        order.AssignNumber("PO-WH1-2601-0001");
        order.ReplaceLines([PurchaseOrderLine.Create(NewProduct(), 5m, null, 2m, null)]);
        if (approved) order.Submit(11, requiresApproval: false, isLarge: false, Now);
        return order;
    }

    [Fact]
    public void A_receipt_from_an_approved_order_is_a_draft_receipt_in_the_orders_warehouse_for_its_supplier_and_keeps_the_link()
    {
        var order = Order(approved: true);

        var receipt = StockDocument.CreateFromPurchaseOrder(order, 3, Now);

        Assert.Equal((DocumentType.Receipt, DocumentStatus.Draft, 7, true), (receipt.Type, receipt.Status, receipt.PurchaseOrderId, receipt.IsLinked));
        Assert.Equal((order.WarehouseId, order.SupplierId), (receipt.WarehouseId, receipt.PartnerId));
    }

    [Fact]
    public void Only_an_approved_order_can_be_received()
    {
        var order = Order(approved: false);

        var e = Assert.Throws<DomainException>(() => StockDocument.CreateFromPurchaseOrder(order, 3, Now));

        Assert.Equal(Messages.PurchaseOrderNotReceivable(order.No, PurchaseOrderStatus.Draft), e.Message);
    }

    [Fact]
    public void An_ordinary_receipt_is_not_linked_and_its_line_names_no_order_line()
    {
        var receipt = NewReceipt(1, (NewProduct(), 5m, 2m));

        Assert.False(receipt.IsLinked);
        Assert.Null(receipt.PurchaseOrderId);
        Assert.Null(Assert.Single(receipt.Lines).PurchaseOrderLineNo);
    }

    [Fact]
    public void A_line_for_an_order_line_keeps_its_number_and_is_priced_at_the_price_it_is_given_not_at_the_average()
    {
        var line = StockDocumentLine.Create(NewProduct(), DocumentType.Receipt, 4m, null, 2.5m, null, costFromAverage: false, purchaseOrderLineNo: 3);

        Assert.Equal((3, false, 2.5m, 10m), (line.PurchaseOrderLineNo, line.CostFromAverage, line.UnitCost, line.Amount));
    }

    [Theory]
    [InlineData(DocumentStatus.Draft, true)]
    [InlineData(DocumentStatus.Submitted, true)]
    [InlineData(DocumentStatus.Approved, true)]
    [InlineData(DocumentStatus.Posted, true)]
    [InlineData(DocumentStatus.Cancelled, false)]
    [InlineData(DocumentStatus.Reversed, false)]
    public void A_receipt_blocks_the_cancel_of_its_order_while_it_is_open_or_posted_and_not_once_it_is_cancelled_or_reversed(DocumentStatus status, bool blocks)
    {
        Assert.Equal(blocks, DocumentStatuses.BlocksPurchaseOrderCancel(status));
    }
}

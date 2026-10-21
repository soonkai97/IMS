using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Purchasing;

/// <summary>PL2.4: the arithmetic of receiving against an order (plan D4, D10). Received is computed per ORDER LINE NUMBER, never per product, in base units, from what the caller says was posted.</summary>
public class PurchaseOrderQuantitiesTests
{
    private static PurchaseOrder Order(params (int Product, decimal Qty, int? Uom)[] lines)
    {
        var order = PurchaseOrder.Create(NewWarehouse(), NewSupplier(), null, 11, Now).WithId(5);
        order.ReplaceLines(lines.Select(l => PurchaseOrderLine.Create(NewProduct(l.Product, $"SKU-{l.Product}", cartonFactor: 12m), l.Qty, l.Uom, 2m, null)));
        return order;
    }

    private static Dictionary<int, ReceivedQuantity> Posted(params (int LineNo, decimal Qty, decimal BaseQty)[] received) =>
        received.ToDictionary(r => r.LineNo, r => new ReceivedQuantity(r.Qty, r.BaseQty));

    [Theory]
    [InlineData("10", "0", "10")]
    [InlineData("10", "6", "4")]
    [InlineData("10", "10", "0")]
    [InlineData("10", "12", "0")] // never below zero, whatever was posted
    public void What_remains_is_the_ordered_quantity_less_what_was_received_and_never_below_zero(string ordered, string received, string remaining)
    {
        Assert.Equal(decimal.Parse(remaining), PurchaseOrderQuantities.Remaining(decimal.Parse(ordered), decimal.Parse(received)));
    }

    [Fact]
    public void A_receipt_within_what_remains_is_not_over()
    {
        var order = Order((1, 10m, null));

        Assert.Empty(PurchaseOrderQuantities.OverReceipts(order.Lines, Posted((1, 6m, 6m)), [new ReceiptQuantity(1, 4m, 4m)]));
    }

    [Fact]
    public void A_receipt_of_more_than_remains_is_named_with_the_figures_in_the_order_lines_unit()
    {
        var order = Order((1, 10m, null));

        var over = Assert.Single(PurchaseOrderQuantities.OverReceipts(order.Lines, Posted((1, 6m, 6m)), [new ReceiptQuantity(1, 5m, 5m)]));

        Assert.Equal(new OverReceipt(1, Ordered: 10m, Received: 6m, Remaining: 4m, Requested: 5m), over);
    }

    [Fact]
    public void The_same_product_on_two_lines_is_judged_per_line_so_one_line_cannot_consume_the_other()
    {
        var order = Order((1, 10m, null), (1, 5m, null)); // two lines, one product

        // line 1 fully received, line 2 untouched: 5 more of line 2 is fine, 1 more of line 1 is not
        var received = Posted((1, 10m, 10m));
        Assert.Empty(PurchaseOrderQuantities.OverReceipts(order.Lines, received, [new ReceiptQuantity(2, 5m, 5m)]));
        Assert.Equal(1, Assert.Single(PurchaseOrderQuantities.OverReceipts(order.Lines, received, [new ReceiptQuantity(1, 1m, 1m)])).LineNo);
    }

    [Fact]
    public void Lines_of_one_receipt_that_name_the_same_order_line_are_added_up_before_the_check()
    {
        var order = Order((1, 10m, null));

        Assert.Single(PurchaseOrderQuantities.OverReceipts(order.Lines, Posted(), [new ReceiptQuantity(1, 6m, 6m), new ReceiptQuantity(1, 5m, 5m)]));
        Assert.Empty(PurchaseOrderQuantities.OverReceipts(order.Lines, Posted(), [new ReceiptQuantity(1, 6m, 6m), new ReceiptQuantity(1, 4m, 4m)]));
    }

    [Fact]
    public void The_check_is_made_in_base_units_and_reports_in_the_ordered_unit()
    {
        var order = Order((1, 2m, CartonId)); // 2 cartons = 24 pieces
        var received = Posted((1, 1m, 12m)); // 1 carton posted

        Assert.Empty(PurchaseOrderQuantities.OverReceipts(order.Lines, received, [new ReceiptQuantity(1, 1m, 12m)]));
        var over = Assert.Single(PurchaseOrderQuantities.OverReceipts(order.Lines, received, [new ReceiptQuantity(1, 2m, 24m)]));
        Assert.Equal((1m, 2m), (over.Remaining, over.Requested)); // in cartons
    }

    [Fact]
    public void A_line_number_the_order_does_not_have_is_left_to_the_caller()
    {
        var order = Order((1, 10m, null));

        Assert.Empty(PurchaseOrderQuantities.OverReceipts(order.Lines, Posted(), [new ReceiptQuantity(9, 99m, 99m)]));
    }

    [Fact]
    public void Several_lines_over_are_all_reported_in_line_order()
    {
        var order = Order((1, 1m, null), (2, 1m, null));

        var over = PurchaseOrderQuantities.OverReceipts(order.Lines, Posted(), [new ReceiptQuantity(2, 5m, 5m), new ReceiptQuantity(1, 5m, 5m)]);

        Assert.Equal([1, 2], over.Select(o => o.LineNo));
    }
}

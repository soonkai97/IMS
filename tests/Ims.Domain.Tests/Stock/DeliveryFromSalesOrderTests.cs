using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>PL3.5: a delivery created from a sales order carries the link for good, only an Approved order can be delivered, a document has at most one link, and the arithmetic of what remains.</summary>
public class DeliveryFromSalesOrderTests
{
    private static SalesOrder Order(bool approved, params (decimal Qty, int Product)[] lines)
    {
        var order = SalesOrder.Create(NewWarehouse(), NewCustomer(), null, 11, Now).WithId(7);
        order.AssignNumber("SO-WH1-2601-0001");
        order.ReplaceLines((lines.Length == 0 ? [(5m, 1)] : lines).Select(l => SalesOrderLine.Create(NewProduct(l.Product, $"SKU-{l.Product}"), l.Qty, null, 2m, null)));
        if (approved) order.Submit(11, requiresApproval: false, isLarge: false, Now);
        return order;
    }

    [Fact]
    public void A_delivery_from_an_approved_order_is_a_draft_issue_with_purpose_sale_in_the_orders_warehouse_for_its_customer_and_keeps_the_link()
    {
        var order = Order(approved: true);

        var delivery = StockDocument.CreateFromSalesOrder(order, 3, Now);

        Assert.Equal((DocumentType.Issue, DocumentStatus.Draft, IssuePurpose.Sale, 7, true), (delivery.Type, delivery.Status, delivery.Purpose, delivery.SalesOrderId, delivery.IsLinked));
        Assert.Equal((order.WarehouseId, order.CustomerId), (delivery.WarehouseId, delivery.PartnerId));
        Assert.Equal(new DocumentLink(DocumentLinkKind.SalesOrder, 7), delivery.Link);
    }

    [Theory]
    [InlineData(SalesOrderStatus.Draft)]
    [InlineData(SalesOrderStatus.Submitted)]
    [InlineData(SalesOrderStatus.Closed)]
    [InlineData(SalesOrderStatus.Cancelled)]
    public void Only_an_approved_order_can_be_delivered(SalesOrderStatus status)
    {
        var order = Order(approved: false).WithPrivate(nameof(SalesOrder.Status), status);

        var e = Assert.Throws<DomainException>(() => StockDocument.CreateFromSalesOrder(order, 3, Now));

        Assert.Equal(Messages.SalesOrderNotDeliverable(order.No, status), e.Message);
    }

    [Fact]
    public void A_document_carries_at_most_one_link_each_factory_sets_only_its_own()
    {
        var delivery = StockDocument.CreateFromSalesOrder(Order(approved: true), 3, Now);
        var purchase = PurchaseOrder.Create(NewWarehouse(), NewSupplier(), null, 11, Now).WithId(9);
        purchase.AssignNumber("PO-WH1-2601-0001");
        purchase.ReplaceLines([PurchaseOrderLine.Create(NewProduct(), 5m, null, 2m, null)]);
        purchase.Submit(11, requiresApproval: false, isLarge: false, Now);
        var receipt = StockDocument.CreateFromPurchaseOrder(purchase, 3, Now);
        var plain = NewReceipt(1, (NewProduct(), 5m, 2m));

        Assert.Equal((7, (int?)null, DocumentLinkKind.SalesOrder), (delivery.SalesOrderId, delivery.PurchaseOrderId, delivery.Link.Kind));
        Assert.Equal(((int?)null, 9, DocumentLinkKind.PurchaseOrder), (receipt.SalesOrderId, receipt.PurchaseOrderId, receipt.Link.Kind));
        Assert.Equal((DocumentLink.None, false), (plain.Link, plain.IsLinked));
        // No method assigns a link again: both ids have a private setter, so only the two factories above can set one, each on a document that starts with none.
        foreach (var name in new[] { nameof(StockDocument.PurchaseOrderId), nameof(StockDocument.SalesOrderId) })
            Assert.True(typeof(StockDocument).GetProperty(name)!.SetMethod!.IsPrivate, name);
    }

    [Fact]
    public void The_customer_and_the_purpose_of_a_delivery_cannot_be_changed_by_a_save()
    {
        var order = Order(approved: true);
        var delivery = StockDocument.CreateFromSalesOrder(order, 3, Now);

        Assert.Equal(Messages.DeliveryHeaderLocked, Assert.Throws<DomainException>(() => delivery.UpdateHeader(NewCustomer(id: 201), IssuePurpose.Sale, null)).Message);
        Assert.Equal(Messages.DeliveryHeaderLocked, Assert.Throws<DomainException>(() => delivery.UpdateHeader(order.Customer, IssuePurpose.Use, null)).Message);
        delivery.UpdateHeader(order.Customer, IssuePurpose.Sale, "note"); // the remark is free
        Assert.Equal("note", delivery.Remark);
    }

    [Fact]
    public void A_delivery_line_for_an_order_line_keeps_its_number_and_is_valued_at_the_average_with_no_price()
    {
        var line = StockDocumentLine.Create(NewProduct(), DocumentType.Issue, 4m, null, null, null, costFromAverage: true, salesOrderLineNo: 3);

        Assert.Equal((3, true, 0m, (int?)null), (line.SalesOrderLineNo, line.CostFromAverage, line.EnteredUnitCost, line.PurchaseOrderLineNo));
    }

    // ----- what remains -----
    private static readonly IReadOnlyDictionary<int, DeliveredQuantity> None = new Dictionary<int, DeliveredQuantity>();

    [Fact]
    public void What_remains_is_the_ordered_quantity_less_the_delivered_one_and_never_below_zero()
    {
        var line = Order(approved: true, (10m, 1)).Lines[0];

        Assert.Equal(10m, SalesOrderQuantities.RemainingBase(line, default));
        Assert.Equal(4m, SalesOrderQuantities.RemainingBase(line, new DeliveredQuantity(6m, 6m)));
        Assert.Equal(0m, SalesOrderQuantities.RemainingBase(line, new DeliveredQuantity(12m, 12m)));
    }

    [Fact]
    public void A_delivery_over_what_remains_is_reported_per_order_line_with_the_figures_and_lines_naming_the_same_order_line_are_added_up()
    {
        var order = Order(approved: true, (10m, 1), (5m, 2));
        var delivered = new Dictionary<int, DeliveredQuantity> { [1] = new(6m, 6m) };

        var over = SalesOrderQuantities.OverDeliveries(order.Lines, delivered, [new DeliveryQuantity(1, 3m, 3m), new DeliveryQuantity(1, 2m, 2m), new DeliveryQuantity(2, 5m, 5m), new DeliveryQuantity(9, 99m, 99m)]);

        var one = Assert.Single(over);
        Assert.Equal(new OverDelivery(1, Ordered: 10m, Delivered: 6m, Remaining: 4m, Requested: 5m), one); // 3 + 2 asked, 4 left; line 2 is within; line 9 is the caller's to refuse
    }

    [Fact]
    public void A_delivery_exactly_at_what_remains_is_not_over()
    {
        var order = Order(approved: true, (10m, 1));

        Assert.Empty(SalesOrderQuantities.OverDeliveries(order.Lines, new Dictionary<int, DeliveredQuantity> { [1] = new(6m, 6m) }, [new DeliveryQuantity(1, 4m, 4m)]));
        Assert.Empty(SalesOrderQuantities.OverDeliveries(order.Lines, None, [new DeliveryQuantity(1, 10m, 10m)]));
    }

    [Theory]
    [InlineData(DocumentStatus.Draft, true)]
    [InlineData(DocumentStatus.Submitted, true)]
    [InlineData(DocumentStatus.Approved, true)]
    [InlineData(DocumentStatus.Posted, true)]
    [InlineData(DocumentStatus.Cancelled, false)]
    [InlineData(DocumentStatus.Reversed, false)]
    public void A_delivery_keeps_its_order_from_being_cancelled_while_it_is_open_or_posted_not_when_it_is_history(DocumentStatus status, bool blocks)
    {
        Assert.Equal(blocks, DocumentStatuses.BlocksSalesOrderCancel(status));
    }

    [Theory]
    [InlineData(SalesOrderStatus.Approved, true, true, true, true)]
    [InlineData(SalesOrderStatus.Approved, false, true, true, false)]   // nothing left
    [InlineData(SalesOrderStatus.Approved, true, false, true, false)]   // no document.create
    [InlineData(SalesOrderStatus.Approved, true, true, false, false)]    // no so.read
    [InlineData(SalesOrderStatus.Submitted, true, true, true, false)]
    [InlineData(SalesOrderStatus.Draft, true, true, true, false)]
    [InlineData(SalesOrderStatus.Closed, true, true, true, false)]
    [InlineData(SalesOrderStatus.Cancelled, true, true, true, false)]
    public void Deliver_needs_an_approved_order_with_something_left_and_both_permissions(SalesOrderStatus status, bool remaining, bool create, bool read, bool expected)
    {
        var permissions = new List<string>();
        if (create) permissions.Add(Permissions.DocumentCreate);
        if (read) permissions.Add(Permissions.SoRead);
        var actor = new ActorRights(1, EffectivePermissions.Compute(permissions));
        var order = new SalesOrderActionContext(status, RequiresApproval: false, IsLarge: false, CreatedBy: 2, SubmittedBy: 2, AnythingRemaining: remaining);

        Assert.Equal(expected, SalesOrderActionPolicy.MayDeliver(actor, order));
        Assert.Equal(expected, SalesOrderActionPolicy.AllowedActions(actor, order, allowSelfApproval: false).Contains(DocumentActions.Deliver));
    }

    [Fact]
    public void A_document_linked_to_both_kinds_of_order_is_corrupt_and_its_link_is_an_error_not_a_guess()
    {
        var delivery = StockDocument.CreateFromSalesOrder(Order(approved: true), 3, Now).WithPrivate(nameof(StockDocument.PurchaseOrderId), 9);

        Assert.Throws<InvalidOperationException>(() => delivery.Link);
    }

    [Fact]
    public void Posting_is_offered_for_a_linked_delivery_like_for_a_receipt_from_a_purchase_order_and_an_ordinary_issue()
    {
        var poster = new ActorRights(1, EffectivePermissions.Compute([Permissions.DocumentCreate, Permissions.DocumentPost, Permissions.DocumentPostApproved, Permissions.SoRead, Permissions.PoRead]));
        var approved = new DocumentActionContext(DocumentStatus.Approved, true, false, 2, 3);
        string[] Offered(LinkedReceiptContext? link) => [.. DocumentActionPolicy.AllowedActions(poster, approved, allowSelfApproval: false, withinApprovalLimit: true, link)];

        Assert.Contains(DocumentActions.Post, Offered(null));
        Assert.Contains(DocumentActions.Post, Offered(new LinkedReceiptContext(true, DocumentLinkKind.PurchaseOrder)));
        Assert.Contains(DocumentActions.Post, Offered(new LinkedReceiptContext(true, DocumentLinkKind.SalesOrder)));
        Assert.DoesNotContain(DocumentActions.Post, Offered(new LinkedReceiptContext(false, DocumentLinkKind.SalesOrder))); // the module is off: the API would refuse it
    }

    [Fact]
    public void The_policy_for_a_linked_delivery_asks_for_so_read_not_po_read()
    {
        var staffWithSoRead = new ActorRights(1, EffectivePermissions.Compute([Permissions.DocumentCreate, Permissions.SoRead]));
        var staffWithPoRead = new ActorRights(1, EffectivePermissions.Compute([Permissions.DocumentCreate, Permissions.PoRead]));
        var draft = new DocumentActionContext(DocumentStatus.Draft, false, false, 1, null);
        string[] Offered(ActorRights a) => [.. DocumentActionPolicy.AllowedActions(a, draft, allowSelfApproval: false, withinApprovalLimit: true, new LinkedReceiptContext(true, DocumentLinkKind.SalesOrder))];

        Assert.Contains(DocumentActions.Edit, Offered(staffWithSoRead));
        Assert.DoesNotContain(DocumentActions.Edit, Offered(staffWithPoRead));
    }
}

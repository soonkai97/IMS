using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Purchasing;

public class PurchaseOrderTests
{
    private static PurchaseOrder NewOrder(Partner? supplier = null, Warehouse? warehouse = null)
    {
        var order = PurchaseOrder.Create(warehouse ?? NewWarehouse(), supplier ?? NewSupplier(), "urgent", Creator, Now);
        order.AssignNumber("PO-WH1-2601-0001");
        return order;
    }

    private static PurchaseOrderLine Line(decimal qty, decimal price, int productId = 1) =>
        PurchaseOrderLine.Create(NewProduct(productId, $"SKU-{productId}"), qty, null, price, null);

    [Fact]
    public void A_new_order_is_a_draft_for_the_warehouse_and_the_supplier_with_nothing_approved()
    {
        var order = NewOrder();

        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
        Assert.Equal((1, 100, Creator, Now, "urgent"), (order.WarehouseId, order.SupplierId, order.CreatedBy, order.CreatedAt, order.Remark));
        Assert.Null(order.SubmittedBy);
        Assert.Null(order.ApprovedBy);
        Assert.False(order.RequiresApproval);
        Assert.Empty(order.Lines);
    }

    [Fact]
    public void A_customer_cannot_be_the_supplier()
    {
        Assert.Equal(Messages.SupplierRequired, Assert.Throws<DomainException>(() => NewOrder(supplier: NewCustomer())).Message);
    }

    [Fact]
    public void An_inactive_supplier_or_warehouse_is_refused()
    {
        Assert.Equal(Messages.PartnerInactive, Assert.Throws<DomainException>(() => NewOrder(supplier: NewSupplier(active: false))).Message);
        Assert.Equal(Messages.WarehouseInactive, Assert.Throws<DomainException>(() => NewOrder(warehouse: NewWarehouse(active: false))).Message);
    }

    [Fact]
    public void A_soft_deleted_supplier_is_refused_like_an_inactive_one()
    {
        var supplier = NewSupplier();
        supplier.MarkDeleted();

        Assert.Equal(Messages.PartnerInactive, Assert.Throws<DomainException>(() => NewOrder(supplier: supplier)).Message);
    }

    [Fact]
    public void The_number_is_assigned_once()
    {
        var order = NewOrder();

        Assert.Throws<DomainException>(() => order.AssignNumber("PO-WH1-2601-0002"));
        Assert.Equal("PO-WH1-2601-0001", order.No);
    }

    [Fact]
    public void The_lines_are_numbered_from_one_in_the_order_given_and_the_total_is_their_sum()
    {
        var order = NewOrder();

        order.ReplaceLines([Line(2m, 10m, 1), Line(1m, 5.55m, 2), Line(3m, 0.1m, 3)]);

        Assert.Equal([1, 2, 3], order.Lines.Select(l => l.LineNo));
        Assert.Equal(20m + 5.55m + 0.30m, order.TotalAmount);
    }

    [Fact]
    public void Replacing_the_lines_replaces_all_of_them_and_renumbers()
    {
        var order = NewOrder();
        order.ReplaceLines([Line(1m, 1m, 1), Line(1m, 1m, 2)]);

        order.ReplaceLines([Line(4m, 2m, 3)]);

        Assert.Single(order.Lines);
        Assert.Equal((1, 8m), (order.Lines[0].LineNo, order.TotalAmount));
    }

    [Fact]
    public void An_order_needs_at_least_one_line_and_at_most_the_limit_and_a_refusal_leaves_the_draft_as_it_was()
    {
        var order = NewOrder();
        order.ReplaceLines([Line(1m, 1m)]);

        Assert.Equal(Messages.AddLine, Assert.Throws<DomainException>(() => order.ReplaceLines([])).Message);
        var tooMany = Enumerable.Range(1, Limits.MaxDocumentLines + 1).Select(i => Line(1m, 1m, i));
        Assert.Throws<DomainException>(() => order.ReplaceLines(tooMany));
        Assert.Single(order.Lines);
    }

    [Fact]
    public void The_header_of_a_draft_can_change_supplier_and_remark_but_the_supplier_must_still_be_a_supplier()
    {
        var order = NewOrder();

        order.UpdateHeader(NewSupplier(id: 101), null);

        Assert.Equal((101, (string?)null), (order.SupplierId, order.Remark));
        Assert.Throws<DomainException>(() => order.UpdateHeader(NewCustomer(), "x"));
        Assert.Equal(101, order.SupplierId);
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Submitted)]
    [InlineData(PurchaseOrderStatus.Approved)]
    [InlineData(PurchaseOrderStatus.Closed)]
    [InlineData(PurchaseOrderStatus.Cancelled)]
    public void Only_a_draft_can_be_edited(PurchaseOrderStatus status)
    {
        var order = NewOrder().WithPrivate(nameof(PurchaseOrder.Status), status);

        var error = Assert.Throws<DomainException>(() => order.ReplaceLines([Line(1m, 1m)]));

        Assert.Equal(Messages.PurchaseOrderNotEditable("PO-WH1-2601-0001", status), error.Message);
        Assert.Throws<DomainException>(() => order.UpdateHeader(NewSupplier(), null));
        Assert.Throws<DomainException>(order.EnsureEditable);
    }

    [Fact]
    public void The_stored_numbers_of_the_states_and_close_kinds_never_change()
    {
        Assert.Equal([0, 1, 2, 3, 4], Enum.GetValues<PurchaseOrderStatus>().Select(s => (int)s));
        Assert.Equal([1, 2], Enum.GetValues<PurchaseOrderCloseKind>().Select(k => (int)k));
    }

    [Fact]
    public void The_purchase_order_number_has_a_prefix_of_its_own_and_the_document_format()
    {
        Assert.Equal("PO-WH1-2610-0007", DocumentNumber.Format(DocumentNumberFormat.PurchaseOrderPrefix, "WH1", 2610, 7));
        Assert.DoesNotContain(DocumentNumberFormat.PurchaseOrderPrefix,
            new[] { DocumentNumberFormat.ReceiptPrefix, DocumentNumberFormat.IssuePrefix, DocumentNumberFormat.TransferPrefix, DocumentNumberFormat.AdjustPrefix });
        Assert.True(DocumentNumberFormat.PurchaseOrderPrefix.Length <= Limits.DocumentPrefixMaxLength);
    }
}

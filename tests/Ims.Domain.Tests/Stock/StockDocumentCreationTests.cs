using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

public class StockDocumentCreationTests
{
    [Fact]
    public void Create_Receipt_StartsAsDraftWithHeaderFilled()
    {
        var warehouse = NewWarehouse(id: 7);
        var supplier = NewSupplier(id: 55);

        var doc = StockDocument.Create(DocumentType.Receipt, warehouse, supplier, IssuePurpose.Scrap, "from truck", Creator, Now);

        Assert.Equal(DocumentStatus.Draft, doc.Status);
        Assert.Equal(DocumentType.Receipt, doc.Type);
        Assert.Equal(7, doc.WarehouseId);
        Assert.Equal(55, doc.PartnerId);
        Assert.Equal("from truck", doc.Remark);
        Assert.Equal(Creator, doc.CreatedBy);
        Assert.Equal(Now, doc.CreatedAt);
        Assert.Null(doc.Purpose); // purpose only applies to issues
        Assert.False(doc.RequiresApproval);
        Assert.Empty(doc.Lines);
    }

    [Fact]
    public void Create_IssueWithoutPurpose_IsRefused()
    {
        var ex = Assert.Throws<DomainException>(() => StockDocument.Create(DocumentType.Issue, NewWarehouse(), NewCustomer(), null, null, Creator, Now));

        Assert.Equal(Messages.PurposeRequired, ex.Message);
    }

    [Theory]
    [InlineData(DocumentType.Transfer)]
    [InlineData(DocumentType.Adjust)]
    [InlineData(DocumentType.PurchaseOrder)]
    public void Create_TypeOtherThanReceiptOrIssue_Throws(DocumentType type)
    {
        var ex = Assert.Throws<DomainException>(() => StockDocument.Create(type, NewWarehouse(), null, null, null, Creator, Now));

        Assert.Equal(Messages.OnlyReceiptAndIssue, ex.Message);
    }

    [Fact]
    public void Create_InactiveWarehouse_Throws()
    {
        var ex = Assert.Throws<DomainException>(() =>
            StockDocument.Create(DocumentType.Receipt, NewWarehouse(active: false), null, null, null, Creator, Now));

        Assert.Equal(Messages.WarehouseInactive, ex.Message);
    }

    [Fact]
    public void Create_ReceiptWithCustomer_Throws()
    {
        var ex = Assert.Throws<DomainException>(() =>
            StockDocument.Create(DocumentType.Receipt, NewWarehouse(), NewCustomer(), null, null, Creator, Now));

        Assert.Equal(Messages.PartnerWrongType(DocumentType.Receipt, PartnerType.Supplier), ex.Message);
    }

    [Fact]
    public void Create_IssueWithSupplier_Throws()
    {
        var ex = Assert.Throws<DomainException>(() =>
            StockDocument.Create(DocumentType.Issue, NewWarehouse(), NewSupplier(), null, null, Creator, Now));

        Assert.Equal(Messages.PartnerWrongType(DocumentType.Issue, PartnerType.Customer), ex.Message);
    }

    [Fact]
    public void Create_InactivePartner_Throws()
    {
        var ex = Assert.Throws<DomainException>(() =>
            StockDocument.Create(DocumentType.Receipt, NewWarehouse(), NewSupplier(active: false), null, null, Creator, Now));

        Assert.Equal(Messages.PartnerInactive, ex.Message);
    }

    [Fact]
    public void Create_WithoutPartner_IsAllowed()
    {
        var doc = StockDocument.Create(DocumentType.Issue, NewWarehouse(), null, IssuePurpose.Sale, null, Creator, Now);

        Assert.Null(doc.PartnerId);
    }

    [Fact]
    public void AssignNumber_SetsDocumentNo()
    {
        var doc = NewReceipt();

        doc.AssignNumber("RCV-WH1-2601-0001");

        Assert.Equal("RCV-WH1-2601-0001", doc.No);
    }

    [Fact]
    public void UpdateHeader_OnDraft_ChangesPartnerPurposeAndRemark()
    {
        var doc = NewIssue();

        doc.UpdateHeader(NewCustomer(id: 300), IssuePurpose.Scrap, "damaged");

        Assert.Equal(300, doc.PartnerId);
        Assert.Equal(IssuePurpose.Scrap, doc.Purpose);
        Assert.Equal("damaged", doc.Remark);
    }

    [Fact]
    public void UpdateHeader_OnIssueWithNullPurpose_IsRefusedAndChangesNothing()
    {
        var doc = NewIssue();

        var ex = Assert.Throws<DomainException>(() => doc.UpdateHeader(null, null, null));

        Assert.Equal(Messages.PurposeRequired, ex.Message);
        Assert.Equal(IssuePurpose.Sale, doc.Purpose);
    }

    [Fact]
    public void UpdateHeader_OnReceipt_IgnoresPurpose()
    {
        var doc = NewReceipt();

        doc.UpdateHeader(NewSupplier(), IssuePurpose.Use, null);

        Assert.Null(doc.Purpose);
    }

    [Fact]
    public void UpdateHeader_WithWrongPartnerType_Throws()
    {
        var doc = NewReceipt();

        Assert.Throws<DomainException>(() => doc.UpdateHeader(NewCustomer(), null, null));
    }

    [Fact]
    public void UpdateHeader_AfterSubmit_Throws()
    {
        var doc = NewReceipt(1, (NewProduct(), 1m, 1m)).Submitted();

        var ex = Assert.Throws<DomainException>(() => doc.UpdateHeader(null, null, null));

        Assert.Equal(Messages.NotEditable(doc.No, DocumentStatus.Submitted), ex.Message);
    }

    [Fact]
    public void ReplaceLines_OnDraft_ReplacesEveryLine()
    {
        var doc = NewReceipt(1, (NewProduct(1), 1m, 1m), (NewProduct(2, "SKU-2"), 2m, 1m));

        doc.ReplaceLines([NewLine(NewProduct(3, "SKU-3"), DocumentType.Receipt, 9m, 1m)]);

        var line = Assert.Single(doc.Lines);
        Assert.Equal(3, line.ProductId);
    }

    [Fact]
    public void ReplaceLines_AfterPost_Throws()
    {
        var doc = NewPostedReceipt();

        Assert.Throws<DomainException>(() => doc.ReplaceLines([]));
    }

    /// <summary>
    /// KNOWN DEFECT (see docs/team/HANDOFFS.md): AddLines has no EnsureEditable guard, unlike ReplaceLines,
    /// so lines can be appended to a Posted document and change its total. A posted document must never be edited.
    /// </summary>
    [Fact]
    [Trait("KnownDefect", "StockDocument.AddLines-unguarded")]
    public void AddLines_AfterPost_Throws()
    {
        var doc = NewPostedReceipt();

        Assert.Throws<DomainException>(() => doc.AddLines([NewLine(NewProduct(2, "SKU-2"), DocumentType.Receipt, 1m, 1m)]));
    }

    [Fact]
    public void Totals_SumBaseQuantityAndValueOfAllLines()
    {
        var doc = NewReceipt(1, (NewProduct(1), 10m, 2.5m), (NewProduct(2, "SKU-2"), 4m, 10m));

        Assert.Equal(14m, doc.TotalQty);
        Assert.Equal(65m, doc.TotalAmount); // 10 x 2.5 + 4 x 10
    }

    [Fact]
    public void Totals_UseBaseQuantity_NotEnteredQuantity()
    {
        var carton = NewProduct(cartonFactor: 12m);
        var doc = StockDocument.Create(DocumentType.Receipt, NewWarehouse(), null, null, null, Creator, Now);
        doc.AddLines([NewLine(carton, DocumentType.Receipt, 2m, 120m, CartonId)]);

        Assert.Equal(24m, doc.TotalQty);
        Assert.Equal(240m, doc.TotalAmount); // 24 pcs x 10 per piece
    }
}

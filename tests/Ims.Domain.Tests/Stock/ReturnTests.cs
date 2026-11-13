using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

public class ReturnTests
{
    private static StockDocument PostedSale(IssuePurpose purpose = IssuePurpose.Sale)
    {
        var doc = StockDocument.Create(DocumentType.Issue, NewWarehouse(), NewCustomer(), purpose, null, Creator, Now).WithId(10);
        doc.AddLines([NewLine(NewProduct(1, "A"), DocumentType.Issue, 8m, lineId: 21), NewLine(NewProduct(2, "B"), DocumentType.Issue, 4m, lineId: 20)]); // keys out of the order given
        doc.Submit(Submitter, requiresApproval: false, Now);
        doc.MarkPosted(Approver, Now);
        return doc;
    }

    // ----- creating the draft -----
    [Fact]
    public void A_customer_return_is_a_receipt_of_a_posted_sale_with_the_sales_customer_and_warehouse_and_the_link()
    {
        var sale = PostedSale();

        var draft = StockDocument.CreateReturn(sale, Creator, Now);

        Assert.Equal((DocumentType.Receipt, DocumentStatus.Draft, null), (draft.Type, draft.Status, draft.Purpose));
        Assert.Equal((sale.WarehouseId, sale.PartnerId, sale.Id), (draft.WarehouseId, draft.PartnerId, draft.ReturnOfDocumentId));
        Assert.True(draft.IsReturn);
        Assert.True(draft.IsLinked);
        Assert.Equal(new DocumentLink(DocumentLinkKind.Return, sale.Id), draft.Link);
    }

    [Fact]
    public void A_supplier_return_is_an_issue_with_purpose_Return_of_a_posted_receipt_with_the_supplier()
    {
        var receipt = NewPostedReceipt();

        var draft = StockDocument.CreateReturn(receipt, Creator, Now);

        Assert.Equal((DocumentType.Issue, IssuePurpose.Return, receipt.PartnerId), (draft.Type, draft.Purpose, draft.PartnerId));
    }

    [Fact]
    public void Only_a_posted_sale_or_receipt_that_is_not_a_return_can_be_returned()
    {
        var draftReceipt = NewReceipt(3, (NewProduct(), 1m, 1m));
        Assert.Equal(Messages.ReturnOriginalNotPosted(draftReceipt.No, DocumentStatus.Draft), Assert.Throws<DomainException>(() => StockDocument.CreateReturn(draftReceipt, Creator, Now)).Message);
        Assert.Throws<DomainException>(() => StockDocument.CreateReturn(PostedSale(IssuePurpose.Use), Creator, Now));
        Assert.Throws<DomainException>(() => StockDocument.CreateReturn(PostedSale(IssuePurpose.Scrap), Creator, Now));

        var posted = NewPostedReceipt();
        var asReturn = StockDocument.CreateReturn(posted, Creator, Now);
        asReturn.AddLines([NewLine(NewProduct(), DocumentType.Issue, 1m)]);
        asReturn.Submit(Submitter, requiresApproval: false, Now);
        asReturn.MarkPosted(Approver, Now);
        Assert.Equal(Messages.ReturnOfAReturn(asReturn.No), Assert.Throws<DomainException>(() => StockDocument.CreateReturn(asReturn, Creator, Now)).Message);
    }

    // ----- the header is locked -----
    [Fact]
    public void A_return_keeps_the_partner_and_the_purpose_of_its_original_and_takes_a_new_remark()
    {
        var customerReturn = StockDocument.CreateReturn(PostedSale(), Creator, Now);
        var supplierReturn = StockDocument.CreateReturn(NewPostedReceipt(), Creator, Now);

        customerReturn.UpdateHeader(NewCustomer(), null, "damaged");     // the same customer (a customer on a receipt: EnsureUsableFor would refuse it)
        supplierReturn.UpdateHeader(NewSupplier(), IssuePurpose.Return, null);

        Assert.Equal("damaged", customerReturn.Remark);
        Assert.Equal(Messages.ReturnHeaderLocked, Assert.Throws<DomainException>(() => customerReturn.UpdateHeader(NewCustomer(201), null, null)).Message);
        Assert.Equal(Messages.ReturnHeaderLocked, Assert.Throws<DomainException>(() => customerReturn.UpdateHeader(null, null, null)).Message);
        Assert.Equal(Messages.ReturnHeaderLocked, Assert.Throws<DomainException>(() => supplierReturn.UpdateHeader(NewSupplier(), IssuePurpose.Use, null)).Message);
    }

    [Fact]
    public void The_purpose_Return_cannot_be_chosen_on_a_new_issue_or_a_saved_one()
    {
        Assert.Equal(Messages.PurposeReturnReserved, Assert.Throws<DomainException>(() => StockDocument.Create(DocumentType.Issue, NewWarehouse(), NewCustomer(), IssuePurpose.Return, null, Creator, Now)).Message);
        var issue = NewIssue(5, (NewProduct(), 1m));
        Assert.Equal(Messages.PurposeReturnReserved, Assert.Throws<DomainException>(() => issue.UpdateHeader(NewCustomer(), IssuePurpose.Return, null)).Message);
    }

    // ----- a document has at most one link -----
    [Fact]
    public void A_document_that_is_not_a_return_has_no_return_link_and_the_three_kinds_are_distinct()
    {
        var plain = NewReceipt(4, (NewProduct(), 1m, 1m));

        Assert.False(plain.IsReturn);
        Assert.Equal(DocumentLink.None, plain.Link);
        Assert.Equal([0, 1, 2, 3], Enum.GetValues<DocumentLinkKind>().Select(k => (int)k).Order());
    }

    // ----- positions -----
    [Fact]
    public void A_line_position_is_its_place_in_key_order_whatever_order_the_lines_were_added_in()
    {
        var sale = PostedSale();

        var positions = DocumentLinePositions.Of(sale.Lines);

        Assert.Equal([(1, 20), (2, 21)], positions.Select(p => (p.LineNo, p.Line.Id)));
        Assert.Equal(20, DocumentLinePositions.At(sale.Lines, 1)!.Id);
        Assert.Equal(21, DocumentLinePositions.At(sale.Lines, 2)!.Id);
        Assert.Null(DocumentLinePositions.At(sale.Lines, 3));
        Assert.Null(DocumentLinePositions.At(sale.Lines, 0));
    }

    [Fact]
    public void With_500_lines_the_positions_run_1_to_500_and_agree_between_the_two_helpers()
    {
        var product = NewProduct();
        var lines = Enumerable.Range(1, 500).Reverse().Select(i => NewLine(product, DocumentType.Issue, i, lineId: i)).ToList(); // added in descending key order

        var positions = DocumentLinePositions.Of(lines);

        Assert.Equal(Enumerable.Range(1, 500), positions.Select(p => p.LineNo));
        Assert.All(positions, p => Assert.Equal(p.LineNo, p.Line.Id));
        Assert.All(new[] { 1, 250, 500 }, n => Assert.Equal(n, DocumentLinePositions.At(lines, n)!.Id));
    }

    // ----- quantities -----
    [Fact]
    public void What_is_left_to_return_is_the_original_less_what_posted_returns_took_never_below_zero()
    {
        Assert.Equal(5m, ReturnQuantities.Remaining(8m, 3m));
        Assert.Equal(0m, ReturnQuantities.Remaining(8m, 9m));
    }

    [Fact]
    public void Returns_that_name_one_original_line_are_added_up_before_they_are_compared_with_what_is_left()
    {
        var positions = DocumentLinePositions.Of(PostedSale().Lines);          // position 1 holds 4, position 2 holds 8
        var returned = new Dictionary<int, ReturnedQuantity> { [2] = new(3m, 3m) };

        Assert.Empty(ReturnQuantities.OverReturns(positions, returned, [new(1, 4m), new(2, 5m)]));
        var over = ReturnQuantities.OverReturns(positions, returned, [new(2, 3m), new(2, 2.5m), new(1, 4m), new(9, 99m)]);

        var one = Assert.Single(over);
        Assert.Equal((2, 8m, 3m, 5m, 5.5m), (one.LineNo, one.Original, one.Returned, one.Remaining, one.Requested)); // a position the original does not have is not judged here
    }

    // ----- costs -----
    [Fact]
    public void A_customer_return_line_takes_the_cost_the_goods_left_at_per_base_unit_and_is_not_at_the_average()
    {
        var product = NewProduct(cartonFactor: 12m);

        var line = StockDocumentLine.CreateReturn(product, DocumentType.Receipt, 2m, CartonId, "back", returnOfLineNo: 3, originalUnitCost: 0.25m);

        Assert.Equal((3, 24m, 0.25m, false), (line.ReturnOfLineNo, line.BaseQty, line.UnitCost, line.CostFromAverage));
        Assert.Equal(6m, line.Amount);            // 24 pieces at 0.25
        Assert.Equal(3m, line.EnteredUnitCost);   // per carton
    }

    [Fact]
    public void A_supplier_return_line_is_an_ordinary_issue_line_valued_at_the_average_and_the_cost_of_a_return_line_is_capped_at_the_line_limit()
    {
        var issue = StockDocumentLine.CreateReturn(NewProduct(), DocumentType.Issue, 5m, null, null, returnOfLineNo: 1, originalUnitCost: 99m);
        Assert.Equal((true, 0m, 1), (issue.CostFromAverage, issue.UnitCost, issue.ReturnOfLineNo)); // the cost passed in is not used: the average is booked at posting

        var huge = StockDocumentLine.CreateReturn(NewProduct(), DocumentType.Receipt, 2_000_000m, null, null, 1, originalUnitCost: 999_999_999m);
        Assert.Equal(Limits.MaxLineAmount, huge.Amount);
    }

    // ----- approval and partners -----
    [Fact]
    public void A_return_is_always_held_for_approval_at_the_standard_tier()
    {
        Assert.Equal((true, false), ApprovalPolicy.ForReturn);
    }

    [Fact]
    public void IsUsableFor_answers_what_EnsureUsableFor_throws_about()
    {
        Assert.True(NewCustomer().IsUsableFor(DocumentType.Issue));
        Assert.False(NewCustomer().IsUsableFor(DocumentType.Receipt));
        Assert.True(NewSupplier().IsUsableFor(DocumentType.Receipt));
        Assert.False(NewSupplier().IsUsableFor(DocumentType.Issue));
        Assert.False(NewCustomer(active: false).IsUsableFor(DocumentType.Issue));
    }

    // ----- base quantity of partial returns (PL3.17) -----
    private static StockDocumentLine OriginalLine(int id, decimal qty, decimal baseQty)
    {
        var line = NewLine(NewProduct(cartonFactor: 0.666667m), DocumentType.Issue, qty, uomId: CartonId, lineId: id);
        typeof(StockDocumentLine).GetProperty(nameof(StockDocumentLine.BaseQty))!.GetSetMethod(true)!.Invoke(line, [baseQty]); // a Posted line as the database holds it
        return line;
    }

    private static StockDocumentLine ReturnLine(decimal qty, int no = 1) =>
        StockDocumentLine.CreateReturn(NewProduct(cartonFactor: 0.666667m), DocumentType.Receipt, qty, CartonId, null, no, originalUnitCost: 0.5m);

    [Fact]
    public void Three_returns_of_one_against_an_original_of_three_cannot_add_up_to_more_base_quantity_than_the_original_the_last_takes_the_rest()
    {
        var original = DocumentLinePositions.Of([OriginalLine(1, 3m, 2.0000m)]);
        var returned = new Dictionary<int, ReturnedQuantity>();
        var parts = new List<decimal>();
        for (var i = 0; i < 3; i++)
        {
            var line = ReturnLine(1m);
            Assert.Equal(0.6667m, line.BaseQty);                                  // 1 x 0.666667 rounded to four decimals
            ReturnBaseAlignment.Apply(original, returned, [line]);
            parts.Add(line.BaseQty);
            returned[1] = new ReturnedQuantity(returned.GetValueOrDefault(1).Qty + 1m, returned.GetValueOrDefault(1).BaseQty + line.BaseQty);
        }

        Assert.Equal([0.6667m, 0.6667m, 0.6666m], parts);
        Assert.Equal(2.0000m, parts.Sum());
    }

    [Fact]
    public void A_line_is_never_given_more_than_is_left_and_the_value_of_a_customer_return_line_follows_the_rebased_quantity()
    {
        var original = DocumentLinePositions.Of([OriginalLine(1, 3m, 2.0000m)]);
        var returned = new Dictionary<int, ReturnedQuantity> { [1] = new(2m, 1.9999m) };          // 2 of 3 are back, 1.9999 of 2.0000
        var last = ReturnLine(1m);

        ReturnBaseAlignment.Apply(original, returned, [last]);

        Assert.Equal(0.0001m, last.BaseQty);
        Assert.Equal(0.0001m * 0.5m, last.UnitCost * last.BaseQty);                                // still valued at its fixed cost per base unit
        var partial = ReturnLine(1m);
        ReturnBaseAlignment.Apply(original, new Dictionary<int, ReturnedQuantity> { [1] = new(1m, 1.9999m) }, [partial]); // not the last piece, but only 0.0001 is left
        Assert.Equal(0.0001m, partial.BaseQty);
    }

    [Fact]
    public void Lines_of_one_return_that_name_the_same_original_line_are_taken_in_order_and_a_line_that_names_no_original_line_is_left_alone()
    {
        var original = DocumentLinePositions.Of([OriginalLine(1, 2m, 1.3333m)]);
        var a = ReturnLine(1m);
        var b = ReturnLine(1m);
        var stray = ReturnLine(1m, no: 9);

        ReturnBaseAlignment.Apply(original, new Dictionary<int, ReturnedQuantity>(), [a, b, stray]);

        Assert.Equal((0.6667m, 0.6666m), (a.BaseQty, b.BaseQty));                                  // 1.3333 in all, the second takes the rest
        Assert.Equal(0.6667m, stray.BaseQty);
    }
}

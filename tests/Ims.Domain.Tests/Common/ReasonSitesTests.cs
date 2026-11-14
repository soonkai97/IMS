using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Common;

/// <summary>
/// The twelve places where an aggregate demands a reason (BR-02, OUT-02), pinned one table: a blank reason is refused with the sentence of that action, a reason that is short once trimmed with
/// <see cref="Messages.ReasonTooShort"/> (or, for a draft cleared by someone else, with that action's one sentence), and a good reason is stored trimmed. Written against the code before the
/// <see cref="Reason"/> value object took the checks over (DDD-08), so it proves the move changed no sentence, no order and no stored text.
/// </summary>
public class ReasonSitesTests
{
    private const int Maker = 11;
    private const int Reviewer = 12;

    /// <summary>One site: what a blank reason says, what a short one says, and a run that performs the action with a given reason and returns the text the aggregate stored.</summary>
    private sealed record Site(string Missing, string Short, Func<string?, string?> Run);

    private static SalesOrder SalesDraft()
    {
        var order = SalesOrder.Create(NewWarehouse(), NewCustomer(), null, Maker, Now);
        order.AssignNumber("SO-WH1-2601-0001");
        order.ReplaceLines([SalesOrderLine.Create(NewProduct(), 5m, null, 10m, null)]);
        return order;
    }

    private static SalesOrder SalesSubmitted()
    {
        var order = SalesDraft();
        order.Submit(Maker, requiresApproval: true, isLarge: false, Now);
        return order;
    }

    private static SalesOrder SalesApproved()
    {
        var order = SalesSubmitted();
        order.Approve(Reviewer, false, Now);
        return order;
    }

    private static PurchaseOrder PurchaseDraft()
    {
        var order = PurchaseOrder.Create(NewWarehouse(), NewSupplier(), null, Maker, Now);
        order.AssignNumber("PO-WH1-2601-0001");
        order.ReplaceLines([PurchaseOrderLine.Create(NewProduct(), 5m, null, 10m, null)]);
        return order;
    }

    private static PurchaseOrder PurchaseSubmitted()
    {
        var order = PurchaseDraft();
        order.Submit(Maker, requiresApproval: true, isLarge: false, Now);
        return order;
    }

    private static PurchaseOrder PurchaseApproved()
    {
        var order = PurchaseSubmitted();
        order.Approve(Reviewer, false, Now);
        return order;
    }

    private static StockDocument OverrideSubmitted()
    {
        var doc = NewReceipt(1, (NewProduct(), 5m, 2m));
        doc.Submit(Submitter, requiresApproval: true, Now, requiresOverride: true);
        return doc;
    }

    private static readonly Dictionary<string, Site> Sites = new()
    {
        ["stock: override on approve"] = new(Messages.OverrideReasonRequired, Messages.ReasonTooShort, reason =>
        {
            var doc = OverrideSubmitted();
            doc.Approve(Approver, allowSelfApproval: false, Now, reason);
            return doc.NegativeStockOverrideReason;
        }),
        ["stock: reject"] = new(Messages.RejectReasonRequired, Messages.ReasonTooShort, reason =>
        {
            var doc = NewSubmittedReceipt(requiresApproval: true);
            doc.Reject(reason!, Approver, allowSelfApproval: false, Now);
            return doc.RejectionReason;
        }),
        ["stock: cancel an approved document"] = new(Messages.CancelApprovedReasonRequired, Messages.ReasonTooShort, reason =>
        {
            var doc = NewApprovedReceipt();
            doc.Cancel(reason);
            return doc.CancelReason;
        }),
        ["stock: reverse"] = new(Messages.ReverseReasonRequired, Messages.ReasonTooShort, reason =>
        {
            var doc = NewPostedReceipt();
            doc.MarkReversed(reason!, Now, Reverser);
            return doc.ReversalReason;
        }),
        ["sales: reject"] = new(Messages.RejectReasonRequired, Messages.ReasonTooShort, reason =>
        {
            var order = SalesSubmitted();
            order.Reject(reason!, Reviewer, allowSelfApproval: false, Now);
            return order.RejectionReason;
        }),
        ["sales: cancel an approved order"] = new(Messages.SalesOrderCancelReasonRequired, Messages.ReasonTooShort, reason =>
        {
            var order = SalesApproved();
            order.Cancel(reason);
            return order.CancelReason;
        }),
        ["sales: cancel another user's draft"] = new(Messages.CancelOthersDraftReasonRequired, Messages.CancelOthersDraftReasonRequired, reason =>
        {
            var order = SalesDraft();
            order.Cancel(reason, byAnotherUser: true);
            return order.CancelReason;
        }),
        ["sales: close short"] = new(Messages.SalesOrderCloseReasonRequired, Messages.ReasonTooShort, reason =>
        {
            var order = SalesApproved();
            order.CloseShort(reason, Reviewer, Now);
            return order.CloseReason;
        }),
        ["purchasing: reject"] = new(Messages.RejectReasonRequired, Messages.ReasonTooShort, reason =>
        {
            var order = PurchaseSubmitted();
            order.Reject(reason!, Reviewer, allowSelfApproval: false, Now);
            return order.RejectionReason;
        }),
        ["purchasing: cancel an approved order"] = new(Messages.PurchaseOrderCancelReasonRequired, Messages.ReasonTooShort, reason =>
        {
            var order = PurchaseApproved();
            order.Cancel(reason);
            return order.CancelReason;
        }),
        ["purchasing: cancel another user's draft"] = new(Messages.CancelOthersDraftReasonRequired, Messages.CancelOthersDraftReasonRequired, reason =>
        {
            var order = PurchaseDraft();
            order.Cancel(reason, byAnotherUser: true);
            return order.CancelReason;
        }),
        ["purchasing: close short"] = new(Messages.PurchaseOrderCloseReasonRequired, Messages.ReasonTooShort, reason =>
        {
            var order = PurchaseApproved();
            order.CloseShort(reason, Reviewer, Now);
            return order.CloseReason;
        }),
    };

    public static TheoryData<string> SiteNames => new(Sites.Keys);

    [Fact]
    public void TheTable_HoldsTwelveSites()
    {
        Assert.Equal(12, Sites.Count);
    }

    [Theory]
    [MemberData(nameof(SiteNames))]
    public void ABlankReason_IsRefusedWithTheSentenceOfTheAction(string site)
    {
        foreach (var blank in new string?[] { null, "", "   ", "\t \n" })
            Assert.Equal(Sites[site].Missing, Assert.Throws<DomainException>(() => Sites[site].Run(blank)).Message);
    }

    [Theory]
    [MemberData(nameof(SiteNames))]
    public void AReasonThatIsShortOnceTrimmed_IsRefusedWithTheTooShortSentence(string site)
    {
        foreach (var shortReason in new[] { "a", "abcd", "  ab  ", "   abcd   " })
            Assert.Equal(Sites[site].Short, Assert.Throws<DomainException>(() => Sites[site].Run(shortReason)).Message);
    }

    [Theory]
    [MemberData(nameof(SiteNames))]
    public void AReasonOfTheMinimumLengthOnceTrimmed_IsStoredTrimmed(string site)
    {
        Assert.Equal("abcde", Sites[site].Run("abcde"));
        Assert.Equal("price looks wrong", Sites[site].Run("  price looks wrong \t"));
        Assert.Equal(new string('x', Limits.ReasonMinLength), Sites[site].Run($"  {new string('x', Limits.ReasonMinLength)}  "));
    }
}

using static Ims.Domain.Tests.Common.OwnershipTable;
using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Common;

/// <summary>
/// DDD-09 part B: the "mine" rule asked of the Domain. The shapes are the ones <see cref="OwnershipAccessTests"/> pinned against the three access services on <c>main</c>, so the same rows now describe
/// <see cref="DocumentOwnership"/> and the <c>Ownership</c> of a stock document, a purchase order and a sales order (which hand over the audit column the services used to read themselves), and
/// <c>IsEditable</c> of both orders.
/// </summary>
public class DocumentOwnershipTests
{
    public static TheoryData<int> DocumentCases() => new(Enumerable.Range(0, All(StatusesOfDocument).Count()));
    public static TheoryData<int> PurchaseCases() => new(Enumerable.Range(0, All(StatusesOfPurchaseOrder).Count()));
    public static TheoryData<int> SalesCases() => new(Enumerable.Range(0, All(StatusesOfSalesOrder).Count()));

    [Theory]
    [MemberData(nameof(DocumentCases))]
    public void ADocumentIsMine_WhenISubmittedItOrAmAndItIsADraftIMadeOrLastChanged(int index)
    {
        var shape = All(StatusesOfDocument).ElementAt(index);

        Assert.Equal(shape.IsMine, DocumentOf(shape).Ownership.IsMine(Me));
    }

    [Theory]
    [MemberData(nameof(PurchaseCases))]
    public void APurchaseOrderIsMine_WhenISubmittedItOrAmAndItIsADraftIMadeOrLastChanged(int index)
    {
        var shape = All(StatusesOfPurchaseOrder).ElementAt(index);

        Assert.Equal(shape.IsMine, PurchaseOrderOf(shape).Ownership.IsMine(Me));
    }

    [Theory]
    [MemberData(nameof(SalesCases))]
    public void ASalesOrderIsMine_WhenISubmittedItOrAmAndItIsADraftIMadeOrLastChanged(int index)
    {
        var shape = All(StatusesOfSalesOrder).ElementAt(index);

        Assert.Equal(shape.IsMine, SalesOrderOf(shape).Ownership.IsMine(Me));
    }

    [Theory]
    [InlineData(true, Me, null, Other, true)]       // I made the Draft
    [InlineData(true, Other, null, Me, true)]       // I changed it last (the recall itself did)
    [InlineData(true, Other, Me, Other, true)]      // I submitted it
    [InlineData(true, Other, Other, Other, false)]
    [InlineData(true, Other, null, null, false)]    // nobody revised it
    [InlineData(false, Me, Other, Me, false)]       // not a Draft: I made it and changed it last, but did not submit it
    [InlineData(false, Other, Me, Other, true)]
    public void TheRuleOnItsOwn(bool isDraft, int createdBy, int? submittedBy, int? lastChangedBy, bool expected)
    {
        var ownership = new DocumentOwnership(createdBy, submittedBy, lastChangedBy, isDraft);

        Assert.Equal(expected, ownership.IsMine(Me));
    }

    // ----- is an order editable -----
    [Theory]
    [InlineData(PurchaseOrderStatus.Draft, true)]
    [InlineData(PurchaseOrderStatus.Submitted, false)]
    [InlineData(PurchaseOrderStatus.Approved, false)]
    [InlineData(PurchaseOrderStatus.Closed, false)]
    [InlineData(PurchaseOrderStatus.Cancelled, false)]
    public void APurchaseOrderIsEditable_OnlyWhileItIsADraft(PurchaseOrderStatus status, bool expected)
    {
        var order = PurchaseOrder.Create(NewWarehouse(), NewSupplier(), null, Creator, Now).WithPrivate("Status", status);

        Assert.Equal(expected, order.IsEditable);
        Assert.Equal(expected, Record.Exception(order.EnsureEditable) is null);
    }

    [Theory]
    [InlineData(SalesOrderStatus.Draft, true)]
    [InlineData(SalesOrderStatus.Submitted, false)]
    [InlineData(SalesOrderStatus.Approved, false)]
    [InlineData(SalesOrderStatus.Closed, false)]
    [InlineData(SalesOrderStatus.Cancelled, false)]
    public void ASalesOrderIsEditable_OnlyWhileItIsADraft(SalesOrderStatus status, bool expected)
    {
        var order = SalesOrder.Create(NewWarehouse(), NewCustomer(), null, Creator, Now).WithPrivate("Status", status);

        Assert.Equal(expected, order.IsEditable);
        Assert.Equal(expected, Record.Exception(order.EnsureEditable) is null);
    }
}

using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Purchasing;
using Ims.Application.Features.Sales;
using Ims.Domain.Tests.Stock;
using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Common;

/// <summary>
/// DDD-09 part B, characterization: when may an order be edited? Only while it is a Draft, written from the rule (PL2.2, PL3.2). Two answers exist for the other statuses and both stay: the update handler
/// answers 409 (the order moved under the caller, who reloads), after the scope check and the version check and before any warehouse lookup; the aggregate itself refuses a change with a domain error
/// (400) in the same sentence. The table runs the handler and the aggregate for every status of both orders, as they are on <c>main</c> and after <c>IsEditable</c> takes the question over.
/// </summary>
public class OrderEditableTests
{
    private const string Number = "SO-WH1-2601-0001";

    public static TheoryData<string> PurchaseStatuses() => new(Enum.GetNames<PurchaseOrderStatus>());
    public static TheoryData<string> SalesStatuses() => new(Enum.GetNames<SalesOrderStatus>());

    private static PurchaseOrder PurchaseOrderIn(string status)
    {
        var order = PurchaseOrder.Create(NewWarehouse(), NewSupplier(), null, Creator, Now);
        order.AssignNumber(Number);
        return order.WithPrivate("Status", Enum.Parse<PurchaseOrderStatus>(status));
    }

    private static SalesOrder SalesOrderIn(string status)
    {
        var order = SalesOrder.Create(NewWarehouse(), NewCustomer(), null, Creator, Now);
        order.AssignNumber(Number);
        return order.WithPrivate("Status", Enum.Parse<SalesOrderStatus>(status));
    }

    // ----- the purchase order -----
    [Theory]
    [MemberData(nameof(PurchaseStatuses))]
    public async Task UpdatingAPurchaseOrder_IsAConflictUnlessItIsADraft(string status)
    {
        var order = PurchaseOrderIn(status);
        var journal = new Journal();
        var handler = new UpdatePurchaseOrderHandler(new FakeRepository<PurchaseOrder>([order], journal), null!, null!, new PurchaseScope(journal), null!, new AllowAll(), new RecordingVersion(journal),
            new StopAtWarehouse<Warehouse>(journal), null!, null!);
        var request = new PurchaseOrderSaveRequest(Guid.NewGuid(), Guid.NewGuid(), null, []);

        var thrown = await Record.ExceptionAsync(() => handler.HandleAsync(new UpdatePurchaseOrderCommand(order.PublicId, request, "token"), CancellationToken.None));

        AssertEditedOnlyAsADraft(status == "Draft", thrown, Messages.PurchaseOrderNotEditable(Number, Enum.Parse<PurchaseOrderStatus>(status)), journal);
    }

    [Theory]
    [MemberData(nameof(PurchaseStatuses))]
    public void APurchaseOrderRefusesAChangeUnlessItIsADraft_InTheSameSentence(string status)
    {
        var order = PurchaseOrderIn(status);

        var checkedAlone = Record.Exception(order.EnsureEditable);
        var header = Record.Exception(() => order.UpdateHeader(NewSupplier(), "remark"));
        var lines = Record.Exception(() => order.ReplaceLines([]));

        if (status == "Draft")
        {
            Assert.Null(checkedAlone);
            Assert.Null(header);
            return;
        }
        var sentence = Messages.PurchaseOrderNotEditable(Number, Enum.Parse<PurchaseOrderStatus>(status));
        foreach (var thrown in new[] { checkedAlone, header, lines }) Assert.Equal(sentence, Assert.IsType<DomainException>(thrown).Message);
    }

    // ----- the sales order -----
    [Theory]
    [MemberData(nameof(SalesStatuses))]
    public async Task UpdatingASalesOrder_IsAConflictUnlessItIsADraft(string status)
    {
        var order = SalesOrderIn(status);
        var journal = new Journal();
        var handler = new UpdateSalesOrderHandler(new FakeRepository<SalesOrder>([order], journal), null!, null!, new SalesScope(journal), null!, new RecordingVersion(journal),
            new StopAtWarehouse<Warehouse>(journal), null!, null!, null!);
        var request = new SalesOrderSaveRequest(Guid.NewGuid(), Guid.NewGuid(), null, []);

        var thrown = await Record.ExceptionAsync(() => handler.HandleAsync(new UpdateSalesOrderCommand(order.PublicId, request, "token"), CancellationToken.None));

        AssertEditedOnlyAsADraft(status == "Draft", thrown, Messages.SalesOrderNotEditable(Number, Enum.Parse<SalesOrderStatus>(status)), journal);
    }

    [Theory]
    [MemberData(nameof(SalesStatuses))]
    public void ASalesOrderRefusesAChangeUnlessItIsADraft_InTheSameSentence(string status)
    {
        var order = SalesOrderIn(status);

        var checkedAlone = Record.Exception(order.EnsureEditable);
        var header = Record.Exception(() => order.UpdateHeader(NewCustomer(), "remark"));
        var lines = Record.Exception(() => order.ReplaceLines([]));

        if (status == "Draft")
        {
            Assert.Null(checkedAlone);
            Assert.Null(header);
            return;
        }
        var sentence = Messages.SalesOrderNotEditable(Number, Enum.Parse<SalesOrderStatus>(status));
        foreach (var thrown in new[] { checkedAlone, header, lines }) Assert.Equal(sentence, Assert.IsType<DomainException>(thrown).Message);
    }

    /// <summary>A Draft goes on to the warehouse lookup (where the test stops it); any other status is a 409 with the sentence, right after the scope and the version, before the lookup.</summary>
    private static void AssertEditedOnlyAsADraft(bool isDraft, Exception? thrown, string sentence, Journal journal)
    {
        if (isDraft)
        {
            Assert.IsType<StopHere>(thrown);
            Assert.Equal(["scope", "version", "warehouse"], journal);
            return;
        }
        Assert.Equal(sentence, Assert.IsType<ConflictException>(thrown).Message);
        Assert.Equal(["scope", "version"], journal);
    }

    // ----- stand-ins: every member the handler does not reach here throws -----
    private sealed class StopAtWarehouse<T>(Journal journal) : IIdMap<T> where T : PublicEntity
    {
        public Task<IReadOnlyDictionary<Guid, int>> ToInternalAsync(IEnumerable<Guid> publicIds, CancellationToken ct = default)
        {
            journal.Add("warehouse");
            throw new StopHere();
        }

        public Task<IReadOnlyDictionary<int, Guid>> ToPublicAsync(IEnumerable<int> ids, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class RecordingVersion(Journal journal) : IDocumentVersion
    {
        public void EnsureCurrent(Guid publicId, string no, AuditableEntity row, string? expectedToken) => journal.Add("version");
        public string Token(Guid publicId, byte[]? rowVersion) => throw new NotSupportedException();
        public void EnsureCurrent(StockDocument document, string? expectedToken) => throw new NotSupportedException();
    }

    private sealed class AllowAll : ICallerPermissions
    {
        public bool Has(string permission) => true;
    }

    private sealed class PurchaseScope(Journal journal) : IPurchaseOrderAccess
    {
        public void EnsureInScope(int warehouseId) => journal.Add("scope");
        public void EnsureWarehouse(int warehouseId) => throw new NotSupportedException();
        public IReadOnlyCollection<int>? WarehouseScope() => throw new NotSupportedException();
        public void EnsureMayRead() => throw new NotSupportedException();
        public void EnsureMayCancel(PurchaseOrder order) => throw new NotSupportedException();
        public void EnsureMayClose(PurchaseOrder order, bool anythingReceived) => throw new NotSupportedException();
        public void EnsureMayRecall(PurchaseOrder order) => throw new NotSupportedException();
        public IReadOnlyList<string> AllowedActions(PurchaseOrderDto order, SystemSettings settings, bool withinApprovalLimit) => throw new NotSupportedException();
        public bool CanAct(PurchaseOrderListItem item, SystemSettings settings) => throw new NotSupportedException();
    }

    private sealed class SalesScope(Journal journal) : ISalesOrderAccess
    {
        public void EnsureInScope(int warehouseId) => journal.Add("scope");
        public void EnsureWarehouse(int warehouseId) => throw new NotSupportedException();
        public IReadOnlyCollection<int>? WarehouseScope() => throw new NotSupportedException();
        public bool MaySeeAmounts() => throw new NotSupportedException();
        public void EnsureMayRead() => throw new NotSupportedException();
        public void EnsureMayCancel(SalesOrder order) => throw new NotSupportedException();
        public void EnsureMayClose(SalesOrder order, bool anythingDelivered) => throw new NotSupportedException();
        public void EnsureMayRecall(SalesOrder order) => throw new NotSupportedException();
        public IReadOnlyList<string> AllowedActions(SalesOrderDto order, SystemSettings settings, bool withinApprovalLimit) => throw new NotSupportedException();
        public bool CanAct(SalesOrderListItem item, SystemSettings settings) => throw new NotSupportedException();
    }
}

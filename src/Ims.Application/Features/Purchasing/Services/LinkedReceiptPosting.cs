using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public class LinkedReceiptPosting(IReadRepository<PurchaseOrder> orders, IPurchaseOrderReceiving receiving, IModuleSwitches modules, ITransactionLocker locker, ICurrentUser current,
    IClock clock, IAuditTrail audit) : ILinkedReceiptPosting, IAppService
{
    public async Task<LinkedPosting?> BeforePostAsync(StockDocument document, CancellationToken ct)
    {
        if (document.PurchaseOrderId is not { } key) return null;

        // First lock of the transaction: nothing has locked stock yet (the balance loader does it after this returns).
        await locker.LockPurchaseOrderAsync(key, ct);
        if (!await modules.IsEnabledAsync(ModuleCodes.Purchasing, ct)) throw new ConflictException(Messages.ReceiptCannotPostModuleOff);
        var order = await orders.FirstOrDefaultAsync(new PurchaseOrderForReceiptSpec(key), ct) ?? throw new NotFoundException(Messages.PurchaseOrderNotFound);
        if (!order.CanBeReceived) throw new ConflictException(Messages.PurchaseOrderNotReceivable(order.No, order.Status));

        var received = await receiving.ReceivedAsync(key, ct);
        var thisReceipt = document.Lines.Where(l => l.PurchaseOrderLineNo is not null).Select(l => new ReceiptQuantity(l.PurchaseOrderLineNo!.Value, l.Qty, l.BaseQty)).ToList();
        var over = PurchaseOrderQuantities.OverReceipts(order.Lines, received, thisReceipt);
        if (over.Count > 0) throw OverReceiptFor(order, over);

        var fullyReceived = order.Lines.All(line =>
            PurchaseOrderQuantities.Remaining(line.BaseQty, received.GetValueOrDefault(line.LineNo).BaseQty + thisReceipt.Where(q => q.LineNo == line.LineNo).Sum(q => q.BaseQty)) == 0m);
        return new LinkedPosting(order, fullyReceived);
    }

    public void AfterPost(LinkedPosting? linked, StockDocument document)
    {
        if (linked is not { FullyReceived: true, Order: var order }) return;
        var before = order.Status;
        order.CloseReceived(current.UserId, clock.UtcNow);
        audit.Record(AuditActions.PurchaseOrderClosed, AuditEntities.PurchaseOrder, order.No, new { Status = before },
            new { order.Status, order.CloseKind, Receipt = document.No });
    }

    private static OverReceiptException OverReceiptFor(PurchaseOrder order, IReadOnlyList<OverReceipt> over)
    {
        var lines = over.Select(o => (Over: o, Line: order.Lines.First(l => l.LineNo == o.LineNo))).ToList();
        var message = string.Join(" ", lines.Select(x => Messages.ReceiptLineOverReceipt(x.Over, x.Line.Product.Sku, x.Line.Uom.Code)));
        return new OverReceiptException(message, lines.Select(x => new OverReceiptLine(x.Over.LineNo, x.Line.Product.Sku, x.Over.Ordered, x.Over.Received, x.Over.Remaining, x.Over.Requested)).ToList());
    }
}

using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public class CreateReceiptFromPurchaseOrderHandler(IReadRepository<PurchaseOrder> orders, IRepository<StockDocument> documents, IPurchaseOrderReceiving receiving, IPurchaseOrderAccess orderAccess,
    IDocumentNumbering numbering, IDocumentReader reader, ICurrentUser current, IClock clock, ITransactionLocker locker, IUnitOfWork uow, IAuditTrail audit)
    : ICommandHandler<CreateReceiptFromPurchaseOrderCommand, DocumentDto>
{
    public async Task<DocumentDto> HandleAsync(CreateReceiptFromPurchaseOrderCommand cmd, CancellationToken ct)
    {
        // The door is document.create; seeing the order it comes from needs po.read as well (one policy per endpoint, so it is asked here, before anything is looked up).
        orderAccess.EnsureMayRead();
        // The whole body re-runs if two requests race for the same document number or the order's lock.
        var publicId = await uow.ExecuteAsync(async () =>
        {
            // The order's lock comes first and the order is read after it: a cancel of the order waits for this receipt (and refuses), or has finished and is seen here (409).
            var key = await orders.FirstOrDefaultAsync(new PurchaseOrderKeySpec(cmd.OrderId), ct) ?? throw new NotFoundException(Messages.PurchaseOrderNotFound);
            await locker.LockPurchaseOrderAsync(key, ct);
            var order = await orders.FirstOrDefaultAsync(new PurchaseOrderForReceiptSpec(key), ct) ?? throw new NotFoundException(Messages.PurchaseOrderNotFound);
            orderAccess.EnsureInScope(order.WarehouseId);
            if (!order.CanBeReceived) throw new ConflictException(Messages.PurchaseOrderNotReceivable(order.No, order.Status));

            // What is left to receive of each line, in the order's own unit: the quantity of the order less what Posted receipts brought in. A line with nothing left is not copied.
            var received = await receiving.ReceivedAsync(order.Id, ct);
            var open = order.Lines.Where(l => PurchaseOrderQuantities.RemainingBase(l, received.GetValueOrDefault(l.LineNo)) > 0).OrderBy(l => l.LineNo).ToList();
            if (open.Count == 0) throw new ConflictException(Messages.PurchaseOrderNothingToReceive(order.No));

            // The receipt takes the order's price whoever creates it: Warehouse staff never see it (the response hides it), but the stock is valued at it (plan D6).
            var receipt = StockDocument.CreateFromPurchaseOrder(order, current.UserId, clock.UtcNow);
            receipt.AssignNumber(await numbering.NextAsync(DocumentType.Receipt, order.Warehouse, ct));
            receipt.AddLines(open.Select(l => StockDocumentLine.Create(l.Product, DocumentType.Receipt, PurchaseOrderQuantities.RemainingQty(l, received.GetValueOrDefault(l.LineNo)), l.UomId,
                l.UnitPrice, l.Remark, costFromAverage: false, purchaseOrderLineNo: l.LineNo)));

            documents.Add(receipt);
            audit.Record(AuditActions.DocumentCreated, AuditEntities.StockDocument, receipt.No,
                after: new { receipt.Type, receipt.WarehouseId, Lines = receipt.Lines.Count, PurchaseOrder = order.No });
            await uow.SaveChangesAsync(ct);
            return receipt.PublicId;
        }, ct);
        return await reader.GetAsync(publicId, ct);
    }
}

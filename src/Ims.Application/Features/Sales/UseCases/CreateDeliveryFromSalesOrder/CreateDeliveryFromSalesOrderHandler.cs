using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public sealed class CreateDeliveryFromSalesOrderHandler(IReadRepository<SalesOrder> orders, IRepository<StockDocument> documents, IDeliveredQuantityReader delivered, ISalesOrderAccess orderAccess,
    IDocumentNumbering numbering, IDocumentReader reader, ICurrentUser current, IClock clock, ITransactionLocker locker, IUnitOfWork uow, IAuditTrail audit)
    : ICommandHandler<CreateDeliveryFromSalesOrderCommand, DocumentDto>
{
    public async Task<DocumentDto> HandleAsync(CreateDeliveryFromSalesOrderCommand cmd, CancellationToken ct)
    {
        // The door is document.create; seeing the order it comes from needs so.read as well (one policy per endpoint, so it is asked here, before anything is looked up).
        orderAccess.EnsureMayRead();
        // The whole body re-runs if two requests race for the same document number or the order's lock.
        var publicId = await uow.ExecuteAsync(async () =>
        {
            // The caller's scope is judged on the key BEFORE any lock is taken, so a caller who may not see the order cannot make others wait on its lock. The order's lock then comes first and the order is
            // read after it, so the quantities and the status seen are the truth for this transaction. (A cancel of the order does not take this lock yet: that, and the refusal while a delivery is open, is PL3.6.)
            var found = await orders.FirstOrDefaultAsync(new SalesOrderKeySpec(cmd.OrderId), ct) ?? throw new NotFoundException(Messages.SalesOrderNotFound);
            orderAccess.EnsureInScope(found.WarehouseId);
            var key = found.Id;
            await locker.LockSalesOrderAsync(key, ct);
            var order = await orders.FirstOrDefaultAsync(new SalesOrderForDeliverySpec(key), ct) ?? throw new NotFoundException(Messages.SalesOrderNotFound);
            if (!order.CanBeDelivered) throw new ConflictException(Messages.SalesOrderNotDeliverable(order.No, order.Status));

            // What is left to deliver of each line, in the order's own unit: the quantity of the order less what Posted deliveries took out. A line with nothing left is not copied.
            var done = await delivered.PostedByLineAsync(order.Id, ct);
            var open = order.Lines.Where(l => SalesOrderQuantities.RemainingQty(l, done.GetValueOrDefault(l.LineNo)) > 0).OrderBy(l => l.LineNo).ToList();
            if (open.Count == 0) throw new ConflictException(Messages.SalesOrderNothingToDeliver(order.No));

            // The delivery carries no price: it is valued at the average cost when it posts, like every issue.
            var delivery = StockDocument.CreateFromSalesOrder(order, current.UserId, clock.UtcNow);
            delivery.AssignNumber(await numbering.NextAsync(DocumentType.Issue, order.Warehouse, ct));
            delivery.AddLines(open.Select(l => StockDocumentLine.Create(l.Product, DocumentType.Issue, SalesOrderQuantities.RemainingQty(l, done.GetValueOrDefault(l.LineNo)), l.UomId,
                null, null, costFromAverage: true, salesOrderLineNo: l.LineNo))); // the order line's remark is the seller's note, not the warehouse's: it is not copied

            documents.Add(delivery);
            audit.Record(AuditActions.DocumentCreated, AuditEntities.StockDocument, delivery.No,
                after: new { delivery.Type, delivery.WarehouseId, Lines = delivery.Lines.Count, SalesOrder = order.No });
            await uow.SaveChangesAsync(ct);
            return delivery.PublicId;
        }, ct);
        return await reader.GetAsync(publicId, ct);
    }
}

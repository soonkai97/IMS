using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class CreateDeliveryFromSalesOrderHandler(IReadRepository<SalesOrder> orders, IRepository<StockDocument> documents, IDeliveredQuantityReader delivered, ISalesOrderAccess orderAccess,
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
            // The order's lock comes first and the order is read after it: a cancel of the order waits for this delivery (and refuses), or has finished and is seen here (409).
            var key = await orders.FirstOrDefaultAsync(new SalesOrderKeySpec(cmd.OrderId), ct) ?? throw new NotFoundException(Messages.SalesOrderNotFound);
            await locker.LockSalesOrderAsync(key, ct);
            var order = await orders.FirstOrDefaultAsync(new SalesOrderForDeliverySpec(key), ct) ?? throw new NotFoundException(Messages.SalesOrderNotFound);
            orderAccess.EnsureInScope(order.WarehouseId);
            if (!order.CanBeDelivered) throw new ConflictException(Messages.SalesOrderNotDeliverable(order.No, order.Status));

            // What is left to deliver of each line, in the order's own unit: the quantity of the order less what Posted deliveries took out. A line with nothing left is not copied.
            var done = await delivered.PostedByLineAsync(order.Id, ct);
            var open = order.Lines.Where(l => SalesOrderQuantities.RemainingBase(l, done.GetValueOrDefault(l.LineNo)) > 0).OrderBy(l => l.LineNo).ToList();
            if (open.Count == 0) throw new ConflictException(Messages.SalesOrderNothingToDeliver(order.No));

            // The delivery carries no price: it is valued at the average cost when it posts, like every issue.
            var delivery = StockDocument.CreateFromSalesOrder(order, current.UserId, clock.UtcNow);
            delivery.AssignNumber(await numbering.NextAsync(DocumentType.Issue, order.Warehouse, ct));
            delivery.AddLines(open.Select(l => StockDocumentLine.Create(l.Product, DocumentType.Issue, SalesOrderQuantities.RemainingQty(l, done.GetValueOrDefault(l.LineNo)), l.UomId,
                null, l.Remark, costFromAverage: true, salesOrderLineNo: l.LineNo)));

            documents.Add(delivery);
            audit.Record(AuditActions.DocumentCreated, AuditEntities.StockDocument, delivery.No,
                after: new { delivery.Type, delivery.WarehouseId, Lines = delivery.Lines.Count, SalesOrder = order.No });
            await uow.SaveChangesAsync(ct);
            return delivery.PublicId;
        }, ct);
        return await reader.GetAsync(publicId, ct);
    }
}

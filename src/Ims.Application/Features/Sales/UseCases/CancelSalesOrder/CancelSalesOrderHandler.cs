using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class CancelSalesOrderHandler(IRepository<SalesOrder> orders, IReadRepository<StockDocument> documents, ISalesOrderAccess access, ISalesOrderReader reader, IDocumentVersion version,
    ITransactionLocker locker, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<CancelSalesOrderCommand, SalesOrderDto>
{
    public async Task<SalesOrderDto> HandleAsync(CancelSalesOrderCommand cmd, CancellationToken ct)
    {
        access.EnsureMayRead();
        await uow.ExecuteAsync(async () =>
        {
            var order = await orders.FirstOrDefaultAsync(new SalesOrderForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.SalesOrderNotFound);
            access.EnsureInScope(order.WarehouseId);
            access.EnsureMayCancel(order);
            version.EnsureCurrent(order.PublicId, order.No, order, cmd.IfMatch);
            if (order.Status == SalesOrderStatus.Approved) await EnsureNoDeliveryAsync(order, ct); // a Draft or Submitted order has no delivery: none can be made before approval

            var before = order.Status;
            order.Cancel(cmd.Data.Reason);
            audit.Record(AuditActions.SalesOrderCancelled, AuditEntities.SalesOrder, order.No, new { Status = before }, new { order.Status, Reason = order.CancelReason });
            await uow.SaveChangesAsync(ct);
            return true;
        }, ct);
        return await reader.GetAsync(cmd.Id, ct);
    }

    /// <summary>
    /// An order with a delivery open (Draft, Submitted, Approved) or Posted against it is not cancelled: the delivery is dealt with first. The check runs under the order's lock, which creating a delivery from the
    /// order takes too (and, from PL3.7, posting one), so a delivery made at the same moment is either seen here or waits and finds the order cancelled.
    /// </summary>
    private async Task EnsureNoDeliveryAsync(SalesOrder order, CancellationToken ct)
    {
        await locker.LockSalesOrderAsync(order.Id, ct);
        var blocking = (await documents.ListAsync(new DeliveriesOfOrderSpec(order.Id), ct)).Where(d => DocumentStatuses.BlocksSalesOrderCancel(d.Status)).ToList();
        if (blocking.Count > 0) throw new ConflictException(Messages.SalesOrderHasDeliveries(order.No, blocking[0].No, blocking[0].Status, blocking.Count));
    }
}

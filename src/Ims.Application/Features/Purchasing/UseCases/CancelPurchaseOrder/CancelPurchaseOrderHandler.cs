using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public class CancelPurchaseOrderHandler(IRepository<PurchaseOrder> orders, IPurchaseOrderReceiving receiving, IPurchaseOrderAccess access, IPurchaseOrderReader reader, IDocumentVersion version,
    ITransactionLocker locker, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<CancelPurchaseOrderCommand, PurchaseOrderDto>
{
    public async Task<PurchaseOrderDto> HandleAsync(CancelPurchaseOrderCommand cmd, CancellationToken ct)
    {
        access.EnsureMayRead();
        await uow.ExecuteAsync(async () =>
        {
            var order = await orders.FirstOrDefaultAsync(new PurchaseOrderForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.PurchaseOrderNotFound);
            access.EnsureInScope(order.WarehouseId);
            access.EnsureMayCancel(order);
            version.EnsureCurrent(order.PublicId, order.No, order, cmd.IfMatch);
            if (order.Status == PurchaseOrderStatus.Approved) await EnsureNoReceiptAsync(order, ct); // a Draft or Submitted order has no receipt: none can be made before approval

            var before = order.Status;
            order.Cancel(cmd.Data.Reason);
            audit.Record(AuditActions.PurchaseOrderCancelled, AuditEntities.PurchaseOrder, order.No, new { Status = before }, new { order.Status, Reason = order.CancelReason });
            await uow.SaveChangesAsync(ct);
            return true;
        }, ct);
        return await reader.GetAsync(cmd.Id, ct);
    }

    /// <summary>
    /// An order with a receipt open (Draft, Submitted, Approved) or Posted against it is not cancelled: the receipt is dealt with first. The check runs under the order's lock, which creating a
    /// receipt from the order takes too, so a receipt made at the same moment is either seen here or waits and finds the order cancelled.
    /// </summary>
    private async Task EnsureNoReceiptAsync(PurchaseOrder order, CancellationToken ct)
    {
        await locker.LockPurchaseOrderAsync(order.Id, ct);
        var blocking = (await receiving.ReceiptsAsync(order.Id, ct)).Where(r => DocumentStatuses.BlocksPurchaseOrderCancel(r.Status)).ToList();
        if (blocking.Count > 0) throw new ConflictException(Messages.PurchaseOrderHasReceipts(order.No, blocking[0].No, blocking[0].Status, blocking.Count));
    }
}

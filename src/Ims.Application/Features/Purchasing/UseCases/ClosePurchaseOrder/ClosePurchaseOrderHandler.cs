using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>Closes an Approved order short: what is still to come will not come (plan D5). Only once something was received: with nothing received the order is cancelled instead (cancel keeps its own authority). Same lock as posting a receipt and cancelling, so it cannot interleave with either.</summary>
public class ClosePurchaseOrderHandler(IRepository<PurchaseOrder> orders, IPurchaseOrderReceiving receiving, IPurchaseOrderAccess access, IPurchaseOrderReader reader, IDocumentVersion version,
    ITransactionLocker locker, ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<ClosePurchaseOrderCommand, PurchaseOrderDto>
{
    public async Task<PurchaseOrderDto> HandleAsync(ClosePurchaseOrderCommand cmd, CancellationToken ct)
    {
        access.EnsureMayRead();
        await uow.ExecuteAsync(async () =>
        {
            var order = await orders.FirstOrDefaultAsync(new PurchaseOrderForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.PurchaseOrderNotFound);
            access.EnsureInScope(order.WarehouseId);
            version.EnsureCurrent(order.PublicId, order.No, order, cmd.IfMatch);

            var anythingReceived = false;
            if (order.Status == PurchaseOrderStatus.Approved)
            {
                // A receipt still waiting would be unpostable once the order is closed: it is posted or cancelled first. Read under the lock that posting and creating a receipt take.
                await locker.LockPurchaseOrderAsync(order.Id, ct);
                var waiting = (await receiving.ReceiptsAsync(order.Id, ct)).Where(r => DocumentStatuses.IsOpen(r.Status)).ToList();
                if (waiting.Count > 0) throw new ConflictException(Messages.PurchaseOrderHasOpenReceipts(order.No, waiting[0].No, waiting[0].Status, waiting.Count));
                anythingReceived = (await receiving.ReceivedAsync(order.Id, ct)).Values.Any(q => q.BaseQty > 0m);
            }

            access.EnsureMayClose(order, anythingReceived);
            var before = order.Status;
            order.CloseShort(cmd.Data.Reason, current.UserId, clock.UtcNow);
            audit.Record(AuditActions.PurchaseOrderClosed, AuditEntities.PurchaseOrder, order.No, new { Status = before }, new { order.Status, order.CloseKind, Reason = order.CloseReason });
            await uow.SaveChangesAsync(ct);
            return true;
        }, ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}

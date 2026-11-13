using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>Closes an Approved order short: what is still to go out will not go (plan D7). Only once something was delivered: with nothing delivered the order is cancelled instead (cancel keeps its own authority). Same lock as posting a delivery and cancelling, so it cannot interleave with either.</summary>
public sealed class CloseSalesOrderHandler(IRepository<SalesOrder> orders, IReadRepository<StockDocument> documents, IDeliveredQuantityReader delivered, ISalesOrderAccess access, ISalesOrderReader reader,
    IDocumentVersion version, ITransactionLocker locker, ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<CloseSalesOrderCommand, SalesOrderDto>
{
    public async Task<SalesOrderDto> HandleAsync(CloseSalesOrderCommand cmd, CancellationToken ct)
    {
        access.EnsureMayRead();
        await uow.ExecuteAsync(async () =>
        {
            var order = await orders.FirstOrDefaultAsync(new SalesOrderForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.SalesOrderNotFound);
            access.EnsureInScope(order.WarehouseId);
            version.EnsureCurrent(order.PublicId, order.No, order, cmd.IfMatch);

            var anythingDelivered = false;
            if (order.Status == SalesOrderStatus.Approved)
            {
                // A delivery still waiting would be unpostable once the order is closed: it is posted or cancelled first. Read under the lock that posting and creating a delivery take.
                await locker.LockSalesOrderAsync(order.Id, ct);
                var waiting = (await documents.ListAsync(new DeliveriesOfOrderSpec(order.Id), ct)).Where(d => DocumentStatuses.IsOpen(d.Status)).ToList();
                if (waiting.Count > 0) throw new ConflictException(Messages.SalesOrderHasOpenDeliveries(order.No, waiting[0].No, waiting[0].Status, waiting.Count));
                anythingDelivered = (await delivered.PostedByLineAsync(order.Id, ct)).Values.Any(q => q.Qty > 0m);
            }

            access.EnsureMayClose(order, anythingDelivered);
            var before = order.Status;
            order.CloseShort(cmd.Data.Reason, current.UserId, clock.UtcNow);
            audit.Record(AuditActions.SalesOrderClosed, AuditEntities.SalesOrder, order.No, new { Status = before }, new { order.Status, order.CloseKind, Reason = order.CloseReason });
            await uow.SaveChangesAsync(ct);
            return true;
        }, ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}

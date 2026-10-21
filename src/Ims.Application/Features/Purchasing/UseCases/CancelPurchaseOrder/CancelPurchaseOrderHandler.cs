using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public class CancelPurchaseOrderHandler(IRepository<PurchaseOrder> orders, IPurchaseOrderAccess access, IPurchaseOrderReader reader, IDocumentVersion version,
    IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<CancelPurchaseOrderCommand, PurchaseOrderDto>
{
    public async Task<PurchaseOrderDto> HandleAsync(CancelPurchaseOrderCommand cmd, CancellationToken ct)
    {
        access.EnsureMayRead();
        var order = await orders.FirstOrDefaultAsync(new PurchaseOrderForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.PurchaseOrderNotFound);
        access.EnsureInScope(order.WarehouseId);
        access.EnsureMayCancel(order);
        version.EnsureCurrent(order.PublicId, order.No, order, cmd.IfMatch);

        // PL2.4 adds the refusal while a linked receipt exists; until receipts can name an order there is nothing to check.
        var before = order.Status;
        order.Cancel(cmd.Data.Reason);
        audit.Record(AuditActions.PurchaseOrderCancelled, AuditEntities.PurchaseOrder, order.No, new { Status = before }, new { order.Status, Reason = order.CancelReason });
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}

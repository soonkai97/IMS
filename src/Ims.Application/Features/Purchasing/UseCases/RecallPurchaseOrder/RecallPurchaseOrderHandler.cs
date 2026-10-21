using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public class RecallPurchaseOrderHandler(IRepository<PurchaseOrder> orders, IPurchaseOrderAccess access, IPurchaseOrderReader reader, IDocumentVersion version,
    IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<RecallPurchaseOrderCommand, PurchaseOrderDto>
{
    public async Task<PurchaseOrderDto> HandleAsync(RecallPurchaseOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.FirstOrDefaultAsync(new PurchaseOrderForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.PurchaseOrderNotFound);
        access.EnsureInScope(order.WarehouseId);
        access.EnsureMayRecall(order);

        // A repeat of a recall that already succeeded (a double tap, a retry) answers with the order as it is, before the version is compared:
        // the retry holds the old token, and nothing is written, so there is no second audit row.
        if (order.IsPlainDraft) return await reader.GetAsync(cmd.Id, ct);

        version.EnsureCurrent(order.PublicId, order.No, order, cmd.IfMatch);
        var before = new { order.Status, order.RequiresApproval };
        order.Recall();
        audit.Record(AuditActions.PurchaseOrderRecalled, AuditEntities.PurchaseOrder, order.No, before, new { order.Status });
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}

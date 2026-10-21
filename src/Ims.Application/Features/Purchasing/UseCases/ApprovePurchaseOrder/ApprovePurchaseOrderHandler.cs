using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public class ApprovePurchaseOrderHandler(IRepository<PurchaseOrder> orders, IApprovalEvaluator approvals, ISystemSettings settings, IPurchaseOrderAccess access,
    IPurchaseOrderReader reader, IDocumentVersion version, ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<ApprovePurchaseOrderCommand, PurchaseOrderDto>
{
    public async Task<PurchaseOrderDto> HandleAsync(ApprovePurchaseOrderCommand cmd, CancellationToken ct)
    {
        access.EnsureMayRead();
        var order = await orders.FirstOrDefaultAsync(new PurchaseOrderForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.PurchaseOrderNotFound);
        access.EnsureInScope(order.WarehouseId);
        version.EnsureCurrent(order.PublicId, order.No, order, cmd.IfMatch);

        // Routing first: only the reviewer of the tier the order was stored with, and not below the rules in force now (the same test as for a document).
        if (await approvals.ReviewRefusalAsync(DocumentType.PurchaseOrder, order.IsLarge, order.TotalQty, order.TotalAmount, ct) is { } refusal)
            throw new ForbiddenException(refusal);

        order.Approve(current.UserId, (await settings.GetAsync(ct)).AllowSelfApproval, clock.UtcNow);
        audit.Record(AuditActions.PurchaseOrderApproved, AuditEntities.PurchaseOrder, order.No, new { Status = PurchaseOrderStatus.Submitted }, new { order.Status, order.IsLarge });
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}

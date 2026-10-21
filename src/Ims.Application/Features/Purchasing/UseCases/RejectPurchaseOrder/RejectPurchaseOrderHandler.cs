using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public class RejectPurchaseOrderHandler(IRepository<PurchaseOrder> orders, IApprovalEvaluator approvals, ISystemSettings settings, IPurchaseOrderAccess access,
    IPurchaseOrderReader reader, IDocumentVersion version, ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<RejectPurchaseOrderCommand, PurchaseOrderDto>
{
    public async Task<PurchaseOrderDto> HandleAsync(RejectPurchaseOrderCommand cmd, CancellationToken ct)
    {
        access.EnsureMayRead();
        var order = await orders.FirstOrDefaultAsync(new PurchaseOrderForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.PurchaseOrderNotFound);
        access.EnsureInScope(order.WarehouseId);
        version.EnsureCurrent(order.PublicId, order.No, order, cmd.IfMatch);

        // Rejecting is a review like approving: only the reviewer the order is routed to may do it (a state error is still reported by the domain below).
        if (order.Status == PurchaseOrderStatus.Submitted && order.RequiresApproval &&
            await approvals.ReviewRefusalAsync(DocumentType.PurchaseOrder, order.IsLarge, order.TotalQty, order.TotalAmount, ct) is { } refusal)
            throw new ForbiddenException(refusal);

        var before = order.Status;
        order.Reject(cmd.Data.Reason, current.UserId, (await settings.GetAsync(ct)).AllowSelfApproval, clock.UtcNow);
        audit.Record(AuditActions.PurchaseOrderRejected, AuditEntities.PurchaseOrder, order.No, new { Status = before }, new { order.Status, Reason = order.RejectionReason });
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}

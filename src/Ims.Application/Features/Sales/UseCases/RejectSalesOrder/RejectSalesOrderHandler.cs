using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class RejectSalesOrderHandler(IRepository<SalesOrder> orders, IApprovalEvaluator approvals, ISystemSettings settings, ISalesOrderAccess access,
    ISalesOrderReader reader, IDocumentVersion version, ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<RejectSalesOrderCommand, SalesOrderDto>
{
    public async Task<SalesOrderDto> HandleAsync(RejectSalesOrderCommand cmd, CancellationToken ct)
    {
        access.EnsureMayRead();
        var order = await orders.FirstOrDefaultAsync(new SalesOrderForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.SalesOrderNotFound);
        access.EnsureInScope(order.WarehouseId);
        version.EnsureCurrent(order.PublicId, order.No, order, cmd.IfMatch);

        // Rejecting is a review like approving: only the reviewer the order is routed to may do it (a state error is still reported by the domain below).
        if (order.Status == SalesOrderStatus.Submitted && order.RequiresApproval &&
            await approvals.ReviewRefusalAsync(DocumentType.SalesOrder, order.IsLarge, order.TotalQty, order.TotalSellingAmount, ct) is { } refusal)
            throw new ForbiddenException(refusal);

        var before = order.Status;
        order.Reject(cmd.Data.Reason, current.UserId, (await settings.GetAsync(ct)).AllowSelfApproval, clock.UtcNow);
        audit.Record(AuditActions.SalesOrderRejected, AuditEntities.SalesOrder, order.No, new { Status = before }, new { order.Status, Reason = order.RejectionReason });
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}

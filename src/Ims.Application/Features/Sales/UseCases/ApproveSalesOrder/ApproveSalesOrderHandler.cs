using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class ApproveSalesOrderHandler(IRepository<SalesOrder> orders, IApprovalEvaluator approvals, ISystemSettings settings, ISalesOrderAccess access,
    ISalesOrderReader reader, IDocumentVersion version, ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<ApproveSalesOrderCommand, SalesOrderDto>
{
    public async Task<SalesOrderDto> HandleAsync(ApproveSalesOrderCommand cmd, CancellationToken ct)
    {
        access.EnsureMayRead();
        var order = await orders.FirstOrDefaultAsync(new SalesOrderForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.SalesOrderNotFound);
        access.EnsureInScope(order.WarehouseId);
        version.EnsureCurrent(order.PublicId, order.No, order, cmd.IfMatch);

        // Routing first: only the reviewer of the tier the order was stored with, and not below the rules in force now (the same test as for a document).
        if (await approvals.ReviewRefusalAsync(DocumentType.SalesOrder, order.IsLarge, order.TotalQty, order.TotalSellingAmount, ct) is { } refusal)
            throw new ForbiddenException(refusal);

        order.Approve(current.UserId, (await settings.GetAsync(ct)).AllowSelfApproval, clock.UtcNow);
        audit.Record(AuditActions.SalesOrderApproved, AuditEntities.SalesOrder, order.No, new { Status = SalesOrderStatus.Submitted }, new { order.Status, order.IsLarge });
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}

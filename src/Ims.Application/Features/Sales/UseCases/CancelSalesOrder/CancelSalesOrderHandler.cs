using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class CancelSalesOrderHandler(IRepository<SalesOrder> orders, ISalesOrderAccess access, ISalesOrderReader reader, IDocumentVersion version, IUnitOfWork uow, IAuditTrail audit)
    : ICommandHandler<CancelSalesOrderCommand, SalesOrderDto>
{
    public async Task<SalesOrderDto> HandleAsync(CancelSalesOrderCommand cmd, CancellationToken ct)
    {
        access.EnsureMayRead();
        var order = await orders.FirstOrDefaultAsync(new SalesOrderForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.SalesOrderNotFound);
        access.EnsureInScope(order.WarehouseId);
        access.EnsureMayCancel(order);
        version.EnsureCurrent(order.PublicId, order.No, order, cmd.IfMatch);

        // No delivery can exist before the delivery link (PL3.5); the refusal while one is open, under the order's lock, arrives with PL3.6.
        var before = order.Status;
        order.Cancel(cmd.Data.Reason);
        audit.Record(AuditActions.SalesOrderCancelled, AuditEntities.SalesOrder, order.No, new { Status = before }, new { order.Status, Reason = order.CancelReason });
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}

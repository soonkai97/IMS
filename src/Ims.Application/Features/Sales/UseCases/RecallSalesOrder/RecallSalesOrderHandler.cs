using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class RecallSalesOrderHandler(IRepository<SalesOrder> orders, ISalesOrderAccess access, ISalesOrderReader reader, IDocumentVersion version,
    IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<RecallSalesOrderCommand, SalesOrderDto>
{
    public async Task<SalesOrderDto> HandleAsync(RecallSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.FirstOrDefaultAsync(new SalesOrderForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.SalesOrderNotFound);
        access.EnsureInScope(order.WarehouseId);
        access.EnsureMayRecall(order);

        // A repeat of a recall that already succeeded (a double tap, a retry) answers with the order as it is, before the version is compared:
        // the retry holds the old token, and nothing is written, so there is no second audit row.
        if (order.IsPlainDraft) return await reader.GetAsync(cmd.Id, ct);

        version.EnsureCurrent(order.PublicId, order.No, order, cmd.IfMatch);
        var before = new { order.Status, order.RequiresApproval };
        order.Recall();
        audit.Record(AuditActions.SalesOrderRecalled, AuditEntities.SalesOrder, order.No, before, new { order.Status });
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}

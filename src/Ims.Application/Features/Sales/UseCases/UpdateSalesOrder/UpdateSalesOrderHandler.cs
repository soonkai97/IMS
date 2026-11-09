using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class UpdateSalesOrderHandler(IRepository<SalesOrder> orders, IReadRepository<Partner> partners, ISalesOrderLineBuilder lineBuilder,
    ISalesOrderAccess access, ISalesOrderReader reader, IDocumentVersion version, IIdMap<Warehouse> warehouseIds, IUnitOfWork uow, IAuditTrail audit, ISalesOrderPriceAudit prices)
    : ICommandHandler<UpdateSalesOrderCommand, SalesOrderDto>
{
    public async Task<SalesOrderDto> HandleAsync(UpdateSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.FirstOrDefaultAsync(new SalesOrderForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.SalesOrderNotFound);
        access.EnsureInScope(order.WarehouseId);
        // The audit of a changed price is exactly-once only because a save names the version it was made against: an empty token skips the check in DocumentVersion, so this write does not run without one.
        if (string.IsNullOrEmpty(cmd.IfMatch)) throw new PreconditionRequiredException(Messages.IfMatchRequired);
        version.EnsureCurrent(order.PublicId, order.No, order, cmd.IfMatch);
        // A state the order is no longer in is a conflict (409), not a bad field: the web reloads the order on a 409.
        if (order.Status != SalesOrderStatus.Draft) throw new ConflictException(Messages.SalesOrderNotEditable(order.No, order.Status));
        if (order.WarehouseId != await warehouseIds.ToInternalOrNoRowAsync(cmd.Data.WarehouseId, ct)) throw new ValidationException(Messages.WarehouseCannotChange);

        var customer = await partners.FirstOrDefaultAsync(new ByPublicIdSpec<Partner>(cmd.Data.CustomerId), ct) ?? throw new ValidationException(Messages.PartnerMissing);
        var before = CreateSalesOrderHandler.Summary(order);
        var savedLines = SalesOrderPriceAudit.Snapshot(order);
        var savedCustomer = order.CustomerId;
        order.UpdateHeader(customer, cmd.Data.Remark);
        order.ReplaceLines(await lineBuilder.BuildAsync(cmd.Data.Lines, ct));

        audit.Record(AuditActions.SalesOrderUpdated, AuditEntities.SalesOrder, order.No, before, CreateSalesOrderHandler.Summary(order));
        await prices.RecordAsync(order, savedLines, savedCustomer, ct);
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}

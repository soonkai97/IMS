using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public class UpdatePurchaseOrderHandler(IRepository<PurchaseOrder> orders, IReadRepository<Partner> partners, IPurchaseOrderLineBuilder lineBuilder,
    IPurchaseOrderAccess access, IPurchaseOrderReader reader, IDocumentVersion version, IIdMap<Warehouse> warehouseIds, IUnitOfWork uow, IAuditTrail audit)
    : ICommandHandler<UpdatePurchaseOrderCommand, PurchaseOrderDto>
{
    public async Task<PurchaseOrderDto> HandleAsync(UpdatePurchaseOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.FirstOrDefaultAsync(new PurchaseOrderForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.PurchaseOrderNotFound);
        access.EnsureInScope(order.WarehouseId);
        version.EnsureCurrent(order.PublicId, order.No, order, cmd.IfMatch);
        // A state the order is no longer in is a conflict (409), not a bad field: the plan asks for it, and the web reloads the order on a 409.
        if (order.Status != PurchaseOrderStatus.Draft) throw new ConflictException(Messages.PurchaseOrderNotEditable(order.No, order.Status));
        if (order.WarehouseId != await warehouseIds.ToInternalOrNoRowAsync(cmd.Data.WarehouseId, ct)) throw new ValidationException(Messages.WarehouseCannotChange);

        var supplier = await partners.FirstOrDefaultAsync(new ByPublicIdSpec<Partner>(cmd.Data.SupplierId), ct) ?? throw new ValidationException(Messages.PartnerMissing);
        var before = CreatePurchaseOrderHandler.Summary(order);
        order.UpdateHeader(supplier, cmd.Data.Remark);
        order.ReplaceLines(await lineBuilder.BuildAsync(cmd.Data.Lines, ct));

        audit.Record(AuditActions.PurchaseOrderUpdated, AuditEntities.PurchaseOrder, order.No, before, CreatePurchaseOrderHandler.Summary(order));
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}

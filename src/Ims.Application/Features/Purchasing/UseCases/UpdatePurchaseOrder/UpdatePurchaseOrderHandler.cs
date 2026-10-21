using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public class UpdatePurchaseOrderHandler(IRepository<PurchaseOrder> orders, IReadRepository<Partner> partners, IPurchaseOrderLineBuilder lineBuilder,
    IPurchaseOrderAccess access, IPurchaseOrderReader reader, ICallerPermissions caller, IDocumentVersion version, IIdMap<Warehouse> warehouseIds, IUnitOfWork uow, IAuditTrail audit)
    : ICommandHandler<UpdatePurchaseOrderCommand, PurchaseOrderDto>
{
    public async Task<PurchaseOrderDto> HandleAsync(UpdatePurchaseOrderCommand cmd, CancellationToken ct)
    {
        // The price of a line is written as sent. A caller who may not see prices would send none (0.00) and overwrite every one, so the permission is asked here and not only by the catalog rule (po.manage requires cost.view).
        if (!caller.Has(Permissions.CostView)) throw new ForbiddenException(Messages.PricesNeedCostView);
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

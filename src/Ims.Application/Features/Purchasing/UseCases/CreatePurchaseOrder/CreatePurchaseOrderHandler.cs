using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public class CreatePurchaseOrderHandler(IRepository<PurchaseOrder> orders, IReadRepository<Warehouse> warehouses, IReadRepository<Partner> partners,
    IDocumentNumbering numbering, IPurchaseOrderLineBuilder lineBuilder, IPurchaseOrderAccess access, IPurchaseOrderReader reader, ICallerPermissions caller,
    ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<CreatePurchaseOrderCommand, PurchaseOrderDto>
{
    public async Task<PurchaseOrderDto> HandleAsync(CreatePurchaseOrderCommand cmd, CancellationToken ct)
    {
        // The price of a line is written as sent. A caller who may not see prices would send none (0.00) and overwrite every one, so the permission is asked here and not only by the catalog rule (po.manage requires cost.view).
        if (!caller.Has(Permissions.CostView)) throw new ForbiddenException(Messages.PricesNeedCostView);
        // The whole body re-runs if two requests race for the same number (BR-11, BR-12).
        var publicId = await uow.ExecuteAsync(async () =>
        {
            var r = cmd.Data;
            var warehouse = await warehouses.FirstOrDefaultAsync(new ByPublicIdSpec<Warehouse>(r.WarehouseId), ct) ?? throw new ValidationException(Messages.WarehouseMissing);
            access.EnsureWarehouse(warehouse.Id);
            var supplier = await partners.FirstOrDefaultAsync(new ByPublicIdSpec<Partner>(r.SupplierId), ct) ?? throw new ValidationException(Messages.PartnerMissing);

            // The aggregate checks the warehouse and the supplier are usable; lines convert units and check limits themselves.
            var order = PurchaseOrder.Create(warehouse, supplier, r.Remark, current.UserId, clock.UtcNow);
            order.AssignNumber(await numbering.NextAsync(DocumentNumberFormat.PurchaseOrderPrefix, warehouse, ct));
            order.ReplaceLines(await lineBuilder.BuildAsync(r.Lines, ct));

            orders.Add(order);
            audit.Record(AuditActions.PurchaseOrderCreated, AuditEntities.PurchaseOrder, order.No, after: Summary(order));
            await uow.SaveChangesAsync(ct);
            return order.PublicId;
        }, ct);
        return await reader.GetAsync(publicId, ct);
    }

    internal static Dictionary<string, object?> Summary(PurchaseOrder order) => new()
    {
        [nameof(order.Status)] = order.Status, [nameof(order.WarehouseId)] = order.WarehouseId, [nameof(order.SupplierId)] = order.SupplierId,
        [nameof(order.Lines)] = order.Lines.Count, [AuditFields.Amount] = order.TotalAmount
    };
}

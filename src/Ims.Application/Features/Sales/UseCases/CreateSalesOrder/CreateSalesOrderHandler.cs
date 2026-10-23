using Ims.Application.Features.Modules;
using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class CreateSalesOrderHandler(IRepository<SalesOrder> orders, IReadRepository<Warehouse> warehouses, IReadRepository<Partner> partners,
    IDocumentNumbering numbering, ISalesOrderLineBuilder lineBuilder, ISalesOrderAccess access, ISalesOrderReader reader,
    ICurrentUser current, IClock clock, ITransactionLocker locker, IModuleSwitches switches, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<CreateSalesOrderCommand, SalesOrderDto>
{
    public async Task<SalesOrderDto> HandleAsync(CreateSalesOrderCommand cmd, CancellationToken ct)
    {
        // The whole body re-runs if two requests race for the same number (BR-11, BR-12).
        var publicId = await uow.ExecuteAsync(async () =>
        {
            // Creating is the one step that adds an open order, which the module's disable check counts: hold the module on (shared lock, switch read again) before anything else is locked.
            await locker.HoldOnAsync(switches, ModuleCodes.Sales, Messages.SalesOrderNotFound, ct);
            var r = cmd.Data;
            var warehouse = await warehouses.FirstOrDefaultAsync(new ByPublicIdSpec<Warehouse>(r.WarehouseId), ct) ?? throw new ValidationException(Messages.WarehouseMissing);
            access.EnsureWarehouse(warehouse.Id);
            var customer = await partners.FirstOrDefaultAsync(new ByPublicIdSpec<Partner>(r.CustomerId), ct) ?? throw new ValidationException(Messages.PartnerMissing);

            // The aggregate checks the warehouse and the customer are usable; lines convert units and check limits themselves.
            var order = SalesOrder.Create(warehouse, customer, r.Remark, current.UserId, clock.UtcNow);
            order.AssignNumber(await numbering.NextAsync(DocumentNumberFormat.SalesOrderPrefix, warehouse, ct));
            order.ReplaceLines(await lineBuilder.BuildAsync(r.Lines, ct));

            orders.Add(order);
            audit.Record(AuditActions.SalesOrderCreated, AuditEntities.SalesOrder, order.No, after: Summary(order));
            await uow.SaveChangesAsync(ct);
            return order.PublicId;
        }, ct);
        return await reader.GetAsync(publicId, ct);
    }

    internal static Dictionary<string, object?> Summary(SalesOrder order) => new()
    {
        [nameof(order.Status)] = order.Status, [nameof(order.WarehouseId)] = order.WarehouseId, [nameof(order.CustomerId)] = order.CustomerId,
        [nameof(order.Lines)] = order.Lines.Count, [AuditFields.Amount] = order.TotalSellingAmount
    };
}

using Ims.Domain;
namespace Ims.Application.Features.Sales;

public sealed class SalesOrderAccess(ICurrentUser current) : ISalesOrderAccess, IAppService
{
    public void EnsureWarehouse(int warehouseId)
    {
        if (!current.CanAccessWarehouse(warehouseId))
            throw new ValidationException(Messages.WarehouseMissing);
    }

    public void EnsureInScope(int warehouseId)
    {
        if (!current.CanAccessWarehouse(warehouseId))
            throw new NotFoundException(Messages.SalesOrderNotFound);
    }

    public IReadOnlyCollection<int>? WarehouseScope() => current.AllWarehouses ? null : current.WarehouseIds;

    public bool MaySeeAmounts() => current.Rights().Has(Permissions.SoManage);

    public void EnsureMayRead()
    {
        if (!current.Rights().Has(Permissions.SoRead)) throw new ForbiddenException(Messages.SalesOrderNeedsSoRead);
    }

    public void EnsureMayCancel(SalesOrder order)
    {
        if (!SalesOrderActionPolicy.MayCancel(current.Rights(), order.ActionContext))
            throw new ForbiddenException(order.Status == SalesOrderStatus.Approved ? Messages.SalesOrderOnlyReviewerCanCancelApproved : Messages.SalesOrderOwnOnly);
    }

    public void EnsureMayClose(SalesOrder order, bool anythingDelivered)
    {
        if (order.Status != SalesOrderStatus.Approved) return; // the state machine refuses it and names the status
        if (SalesOrderActionPolicy.MayClose(current.Rights(), order.ActionContext with { AnythingDelivered = anythingDelivered })) return;
        throw current.Rights().Has(Permissions.SoManage) ? new ConflictException(Messages.SalesOrderNothingDelivered(order.No)) : new ForbiddenException(Messages.SalesOrderNeedsSoManage);
    }

    public void EnsureMayRecall(SalesOrder order)
    {
        // Whoever submitted it. Once recalled an order no longer names its submitter, so the person who made the Draft or last changed it (the recall itself did) counts too:
        // their retry is the same request again. Anyone else is refused in every status, so the answer does not tell what state the order is in.
        var me = current.UserId;
        var mine = order.SubmittedBy == me || (order.Status == SalesOrderStatus.Draft && (order.CreatedBy == me || order.RecRevisor == me));
        if (!mine || !current.Rights().Has(Permissions.SoManage))
            throw new ForbiddenException(Messages.SalesOrderOwnRecallOnly);
    }

    public IReadOnlyList<string> AllowedActions(SalesOrderDto o, SystemSettings settings, bool withinApprovalLimit) =>
        SalesOrderActionPolicy.AllowedActions(current.Rights(), new SalesOrderActionContext(o.Status, o.RequiresApproval, o.IsLarge, o.CreatedByKey, o.SubmittedByKey, o.Lines.Any(l => l.RemainingQty > 0m), o.Lines.Any(l => l.DeliveredQty > 0m),
                o.Deliveries.Any(d => DocumentStatuses.BlocksSalesOrderCancel(d.Status)), o.Deliveries.Any(d => DocumentStatuses.IsOpen(d.Status))),
            settings.AllowSelfApproval, withinApprovalLimit);

    public bool CanAct(SalesOrderListItem item, SystemSettings settings) =>
        SalesOrderActionPolicy.MayReview(current.Rights(), new SalesOrderActionContext(item.Status, item.RequiresApproval, item.IsLarge, item.CreatedByKey, item.SubmittedByKey),
            settings.AllowSelfApproval);
}

using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class SalesOrderAccess(ICurrentUser current) : ISalesOrderAccess, IAppService
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

    public void EnsureMayRead()
    {
        if (!current.Rights().Has(Permissions.SoRead)) throw new ForbiddenException(Messages.SalesOrderNeedsSoRead);
    }

    public void EnsureMayCancel(SalesOrder order)
    {
        if (!SalesOrderActionPolicy.MayCancel(current.Rights(), order.ActionContext))
            throw new ForbiddenException(order.Status == SalesOrderStatus.Approved ? Messages.SalesOrderOnlyReviewerCanCancelApproved : Messages.SalesOrderOwnOnly);
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
        SalesOrderActionPolicy.AllowedActions(current.Rights(), new SalesOrderActionContext(o.Status, o.RequiresApproval, o.IsLarge, o.CreatedByKey, o.SubmittedByKey),
            settings.AllowSelfApproval, withinApprovalLimit);

    public bool CanAct(SalesOrderListItem item, SystemSettings settings) =>
        SalesOrderActionPolicy.MayReview(current.Rights(), new SalesOrderActionContext(item.Status, item.RequiresApproval, item.IsLarge, item.CreatedByKey, item.SubmittedByKey),
            settings.AllowSelfApproval);
}

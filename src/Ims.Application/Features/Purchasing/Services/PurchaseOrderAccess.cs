using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

public class PurchaseOrderAccess(ICurrentUser current) : IPurchaseOrderAccess, IAppService
{
    public void EnsureWarehouse(int warehouseId)
    {
        if (!current.CanAccessWarehouse(warehouseId))
            throw new ValidationException(Messages.WarehouseMissing);
    }

    public void EnsureInScope(int warehouseId)
    {
        if (!current.CanAccessWarehouse(warehouseId))
            throw new NotFoundException(Messages.PurchaseOrderNotFound);
    }

    public IReadOnlyCollection<int>? WarehouseScope() => current.AllWarehouses ? null : current.WarehouseIds;

    public void EnsureMayRead()
    {
        if (!current.Rights().Has(Permissions.PoRead)) throw new ForbiddenException(Messages.PurchaseOrderNeedsPoRead);
    }

    public void EnsureMayCancel(PurchaseOrder order)
    {
        if (!PurchaseOrderActionPolicy.MayCancel(current.Rights(), order.ActionContext))
            throw new ForbiddenException(order.Status == PurchaseOrderStatus.Approved ? Messages.PurchaseOrderOnlyReviewerCanCancelApproved : Messages.PurchaseOrderOwnOnly);
    }

    public void EnsureMayRecall(PurchaseOrder order)
    {
        // Whoever submitted it. Once recalled an order no longer names its submitter, so the person who made the Draft or last changed it (the recall itself did) counts too:
        // their retry is the same request again. Anyone else is refused in every status, so the answer does not tell what state the order is in.
        var me = current.UserId;
        var mine = order.SubmittedBy == me || (order.Status == PurchaseOrderStatus.Draft && (order.CreatedBy == me || order.RecRevisor == me));
        if (!mine || !current.Rights().Has(Permissions.PoManage))
            throw new ForbiddenException(Messages.PurchaseOrderOwnRecallOnly);
    }


    public IReadOnlyList<string> AllowedActions(PurchaseOrderDto o, SystemSettings settings, bool withinApprovalLimit) =>
        PurchaseOrderActionPolicy.AllowedActions(current.Rights(), new PurchaseOrderActionContext(o.Status, o.RequiresApproval, o.IsLarge, o.CreatedByKey, o.SubmittedByKey),
            settings.AllowSelfApproval, withinApprovalLimit);

    public bool CanAct(PurchaseOrderListItem item, SystemSettings settings) =>
        PurchaseOrderActionPolicy.MayReview(current.Rights(), new PurchaseOrderActionContext(item.Status, item.RequiresApproval, item.IsLarge, item.CreatedByKey, item.SubmittedByKey),
            settings.AllowSelfApproval);
}

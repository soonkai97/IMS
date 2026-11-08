using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>The warehouse data scope of the current caller for sales orders. What the caller may do with an order is decided by <see cref="SalesOrderActionPolicy"/>.</summary>
public interface ISalesOrderAccess
{
    /// <summary>A write names a warehouse the caller may not use: the same 400 as a document.</summary>
    /// <exception cref="ValidationException">The warehouse is outside the caller's scope.</exception>
    void EnsureWarehouse(int warehouseId);

    /// <summary>An order outside the caller's warehouses does not exist for them: 404, like an unknown id.</summary>
    /// <exception cref="NotFoundException">The order's warehouse is outside the caller's scope.</exception>
    void EnsureInScope(int warehouseId);

    /// <summary>The warehouses the caller may read; null means all of them.</summary>
    IReadOnlyCollection<int>? WarehouseScope();

    /// <summary>True when the caller may see selling prices and amounts (<c>so.manage</c>). The rule numbers of an approval trigger are the order total at selling price, so they follow it.</summary>
    bool MaySeeAmounts();

    /// <summary>The door of approve, reject and cancel is the approval tier or so.manage; seeing the order they act on needs so.read as well (one policy per endpoint, so this is asked here, before the order is looked up).</summary>
    /// <exception cref="ForbiddenException">The caller does not hold so.read.</exception>
    void EnsureMayRead();

    /// <exception cref="ForbiddenException">The caller may not cancel this order now: a Draft only by its creator (or a holder of modules.manage), a Submitted order only by its submitter, an Approved one only by a reviewer it is routed to.</exception>
    void EnsureMayCancel(SalesOrder order);

    /// <summary>An order is taken back only by its submitter (with <c>so.manage</c>). A Draft counts as theirs too when they made it or last changed it (a recall names no submitter afterwards, so a retry must still be theirs).</summary>
    /// <exception cref="ForbiddenException">The caller did not submit the order.</exception>
    void EnsureMayRecall(SalesOrder order);

    /// <param name="withinApprovalLimit">False when the approval rules in force need a higher approver than the caller's: approve and reject are then not offered.</param>
    IReadOnlyList<string> AllowedActions(SalesOrderDto order, SystemSettings settings, bool withinApprovalLimit);

    /// <summary>True when the caller may approve or reject this listed order now (it is routed to their tier and is not their own submission).</summary>
    bool CanAct(SalesOrderListItem item, SystemSettings settings);
}

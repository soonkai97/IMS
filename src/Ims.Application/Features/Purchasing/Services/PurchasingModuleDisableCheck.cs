using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>
/// Purchasing cannot be turned off while an order is open (Draft, Submitted or Approved): its screens, numbers and approvals would vanish with the order half done (ADR modules-and-tracking-dimensions
/// 4.4). Closed and Cancelled orders do not count; they are kept and shown again when the module is turned on. The count covers every warehouse (an Administrator flips it for the whole installation)
/// and takes no lock of its own: the flip holds the <c>modules</c> lock, and order creation holds its shared form (<see cref="ModuleLocks.HoldOnAsync"/>), so the count and the commit cannot be split by a new order.
/// </summary>
public class PurchasingModuleDisableCheck(IReadRepository<PurchaseOrder> orders) : IModuleDisableCheck, IAppService
{
    public string ModuleCode => ModuleCodes.Purchasing;

    public Task<int> CountBlockersAsync(CancellationToken ct) => orders.CountAsync(new OpenPurchaseOrdersCountSpec(), ct);

    public string Describe(int count) => Messages.PurchaseOrdersOpen(count);
}

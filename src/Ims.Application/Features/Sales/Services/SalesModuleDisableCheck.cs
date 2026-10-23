using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>
/// Sales cannot be turned off while an order is open (Draft, Submitted or Approved): its screens, numbers and approvals would vanish with the order half done (ADR modules-and-tracking-dimensions
/// 4.4). Closed and Cancelled orders do not count; they are kept and shown again when the module is turned on. The count covers every warehouse and takes no lock of its own: the flip holds the
/// <c>modules</c> lock, and order creation holds its shared form (<see cref="ModuleLocks.HoldOnAsync"/>), so the count and the commit cannot be split by a new order.
/// </summary>
public class SalesModuleDisableCheck(IReadRepository<SalesOrder> orders) : IModuleDisableCheck, IAppService
{
    public string ModuleCode => ModuleCodes.Sales;

    public Task<int> CountBlockersAsync(CancellationToken ct) => orders.CountAsync(new OpenSalesOrdersCountSpec(), ct);

    public string Describe(int count) => Messages.SalesOrdersOpen(count);
}

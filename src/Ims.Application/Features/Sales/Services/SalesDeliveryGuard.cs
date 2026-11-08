using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>Turns the answer of <see cref="LinkedReceiptPolicy"/> into the refusal the API gives for a delivery created from a sales order, so the handlers that guard it use the same rule as its <c>allowedActions</c>.</summary>
public static class SalesDeliveryGuard
{
    /// <summary>Does nothing when the action is not refused for the caller. Run before the order is read, so a refusal never quotes the order.</summary>
    /// <param name="action">A <see cref="DocumentActions"/> value.</param>
    /// <exception cref="ForbiddenException">Edit without <c>so.read</c> (403).</exception>
    /// <exception cref="ConflictException">The Sales module is off (409).</exception>
    public static async Task EnsureAsync(string action, ICallerPermissions caller, IModuleSwitches modules, CancellationToken ct)
    {
        var refusal = LinkedReceiptPolicy.Refusal(action, caller.Has(Permissions.SoRead), await modules.IsEnabledAsync(ModuleCodes.Sales, ct));
        switch (refusal)
        {
            case LinkedReceiptRefusal.NeedsPoRead: throw new ForbiddenException(Messages.DeliveryNeedsSoRead);
            case LinkedReceiptRefusal.ModuleOff: throw new ConflictException(action == DocumentActions.Post ? Messages.DeliveryCannotPostModuleOff : Messages.DeliveryModuleOff);
        }
    }
}

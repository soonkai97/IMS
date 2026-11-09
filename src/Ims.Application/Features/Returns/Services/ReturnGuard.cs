using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Returns;

/// <summary>Turns the answer of <see cref="LinkedReceiptPolicy"/> into the refusal the API gives for a return, so the handlers that guard it use the same rule as its <c>allowedActions</c>. Cancelling never asks: a return left behind when the module is switched off can always be cancelled.</summary>
public static class ReturnGuard
{
    /// <summary>Does nothing when the action is not refused for the caller. Run before the original is read, so a refusal never quotes it.</summary>
    /// <exception cref="ForbiddenException">Edit without <c>returns.create</c> (403).</exception>
    /// <exception cref="ConflictException">The Returns module is off (409).</exception>
    public static async Task EnsureAsync(string action, ICallerPermissions caller, IModuleSwitches modules, CancellationToken ct)
    {
        var refusal = LinkedReceiptPolicy.Refusal(action, caller.Has(Permissions.ReturnsCreate), await modules.IsEnabledAsync(ModuleCodes.Returns, ct));
        switch (refusal)
        {
            case LinkedReceiptRefusal.NeedsPoRead: throw new ForbiddenException(Messages.ReturnNeedsReturnsCreate);
            case LinkedReceiptRefusal.ModuleOff: throw new ConflictException(action == DocumentActions.Post ? Messages.ReturnCannotPostModuleOff : Messages.ReturnModuleOff);
        }
    }
}

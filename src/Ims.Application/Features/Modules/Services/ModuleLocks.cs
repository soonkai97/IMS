namespace Ims.Application.Features.Modules;

public static class ModuleLocks
{
    /// <summary>
    /// Holds a module on for the rest of the transaction, for a write that adds data the module's <see cref="IModuleDisableCheck"/> counts. It takes the <c>modules</c> lock in its shared form, so a
    /// flip (which holds the exclusive form from its count to its commit) and the write never overlap, and then reads the switch again from the database: the request gate read it earlier,
    /// before this transaction began, and a flip may have been committed since. Off now, the write is refused like any route of an off module (404) and nothing is written.
    /// Call it first in the transaction, before any other lock (the document-sequence lock comes after it).
    /// </summary>
    /// <exception cref="NotFoundException">The module was turned off after the request passed the gate.</exception>
    public static async Task HoldOnAsync(this ITransactionLocker locker, IModuleSwitches switches, string moduleCode, string notFoundMessage, CancellationToken ct)
    {
        await locker.LockModulesSharedAsync(ct);
        switches.Invalidate();
        if (!await switches.IsEnabledAsync(moduleCode, ct)) throw new NotFoundException(notFoundMessage);
    }
}

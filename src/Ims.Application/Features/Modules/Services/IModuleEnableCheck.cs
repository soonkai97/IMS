namespace Ims.Application.Features.Modules;

/// <summary>
/// The seam by which a module refuses to be turned ON until it can work safely (purchasing needs an active approval rule, or every priced order would be released with no approver). The
/// counterpart of <see cref="IModuleDisableCheck"/>: registered as an <see cref="IAppService"/>, asked by <see cref="SwitchModuleHandler"/> only when a module that is off is being turned on,
/// under the <c>modules</c> lock, before anything is written. A module without one is never refused. It takes no lock of its own.
/// </summary>
public interface IModuleEnableCheck
{
    /// <summary>The code of the module this check guards (a code of <see cref="Ims.Domain.ModuleCatalog"/>).</summary>
    string ModuleCode { get; }

    /// <summary>Null when the module may be turned on; otherwise the sentence that says what to fix first.</summary>
    Task<string?> RefusalAsync(CancellationToken ct);
}

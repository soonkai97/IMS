namespace Ims.Application.Features.Modules;

/// <summary>
/// The seam by which a module refuses to be turned off while its data would be left with nothing to show it (ADR modules-and-tracking-dimensions 4.4: purchasing refuses while any
/// order is open). A module that holds such data registers one check, which is also an <see cref="IAppService"/> so that the convention registers it; a module without one is never
/// refused. <see cref="SwitchModuleHandler"/> asks it only when a module that is on is being turned off, under the <c>modules</c> lock, before anything is written. Turning on is never checked.
/// </summary>
public interface IModuleDisableCheck
{
    /// <summary>The code of the module this check guards (a code of <see cref="Ims.Domain.ModuleCatalog"/>).</summary>
    string ModuleCode { get; }

    /// <summary>How many things would be orphaned if the module were off now; 0 lets the switch go through.</summary>
    Task<int> CountBlockersAsync(CancellationToken ct);

    /// <summary>The count as a plain clause for the refusal, for example "3 purchase orders are still open". Called only with a count above 0.</summary>
    string Describe(int count);
}

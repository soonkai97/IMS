namespace Ims.Application.Features.Modules;

/// <summary>
/// Which modules this installation has on. One small read per request, remembered for the rest of it. A module is on only when it is in the catalog AND its setting row says
/// so: a code the catalog does not know and a module without a row are off. The core has no switch and is always on.
/// </summary>
public interface IModuleSwitches
{
    Task<bool> IsEnabledAsync(string code, CancellationToken ct);

    /// <summary>The codes of the modules that are on, in catalog order (the list the web reads at sign-in).</summary>
    Task<IReadOnlyList<string>> EnabledAsync(CancellationToken ct);

    /// <summary>Call after changing a switch; the next question reads the rows again.</summary>
    void Invalidate();
}

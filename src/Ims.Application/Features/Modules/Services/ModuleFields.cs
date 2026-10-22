using Ims.Domain;
namespace Ims.Application.Features.Modules;

/// <summary>
/// The rule for an optional field of a module on a core endpoint (ADR modules-and-tracking-dimensions 4.3): accepted only while the module is on. Off, a request
/// that carries a value is a 400 naming the field, and a save that carries nothing is fine and must leave the stored values alone (the caller skips the write when this says no).
/// </summary>
public static class ModuleFields
{
    /// <summary>True when the module is on (apply the fields). False when it is off and nothing is carried (leave them). Throws a <see cref="ValidationException"/> when it is off and a non-blank value is carried.</summary>
    public static async Task<bool> AcceptAsync(this IModuleSwitches modules, string moduleCode, IReadOnlyList<(string Field, string? Value)> carried, CancellationToken ct)
    {
        if (await modules.IsEnabledAsync(moduleCode, ct)) return true;
        var used = carried.Where(c => !string.IsNullOrWhiteSpace(c.Value)).Select(c => c.Field).ToList();
        if (used.Count == 0) return false;
        throw new ValidationException(Messages.ModuleFieldNotAvailable(used[0], moduleCode), used.ToDictionary(f => f, f => new[] { Messages.ModuleFieldNotAvailable(f, moduleCode) }));
    }
}

namespace Ims.Domain;

/// <summary>
/// Which permissions are offered while some modules are off, and how a role save keeps the grants the editor cannot see (ADR modules-and-tracking-dimensions 4.3). A permission of a
/// module that is off is not listed and not accepted, but a role that already holds it keeps it: a save from a client that never saw the grant must not delete it, so the saved set
/// is what was submitted plus what the role holds of the permissions that are not offered. Pure functions of the catalog and of the codes of the modules that are on.
/// </summary>
public static class ModulePermissions
{
    /// <summary>A core permission is always offered; a module permission only while its module is on. A code the catalog does not know is not a module permission (the set rules refuse it elsewhere).</summary>
    public static bool IsOffered(string code, IReadOnlyCollection<string> enabledModules) =>
        PermissionCatalog.Find(code)?.Module is not { } module || enabledModules.Contains(module, StringComparer.Ordinal);

    /// <summary>The codes among <paramref name="codes"/> that are not offered now (their module is off), in catalog order.</summary>
    public static IReadOnlyList<string> NotOffered(IEnumerable<string> codes, IReadOnlyCollection<string> enabledModules) =>
        PermissionCatalog.InOrder(codes.Where(c => !IsOffered(c, enabledModules)));

    /// <summary>The codes among <paramref name="codes"/> that are offered now, in catalog order.</summary>
    public static IReadOnlyList<string> Offered(IEnumerable<string> codes, IReadOnlyCollection<string> enabledModules) =>
        PermissionCatalog.InOrder(codes.Where(c => IsOffered(c, enabledModules)));

    /// <summary>
    /// The set a role is saved with: the submitted codes plus what the role holds of the permissions that are not offered. The submitted codes are expected to be offered ones (the caller refuses
    /// the others first); a hidden code in the submission changes nothing here, because the role's own hidden grants are added back anyway.
    /// </summary>
    public static IReadOnlyList<string> Merge(IEnumerable<string> submitted, IEnumerable<string> held, IReadOnlyCollection<string> enabledModules) =>
        PermissionCatalog.InOrder(submitted.Concat(NotOffered(held, enabledModules)).Distinct(StringComparer.Ordinal));
}

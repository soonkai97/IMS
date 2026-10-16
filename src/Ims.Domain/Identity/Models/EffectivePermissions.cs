namespace Ims.Domain;

/// <summary>
/// The permissions a person holds right now: the one answer every check reads (endpoint policies, Domain policies, the user's own list). Pure and
/// built in one place, <see cref="Compute"/>. Today the only input is the grants of the person's role; per-user exceptions are deferred (lean scope),
/// and would be one more input here without changing any caller.
/// </summary>
public sealed class EffectivePermissions
{
    public static readonly EffectivePermissions None = new([]);

    private readonly HashSet<string> _codes;

    private EffectivePermissions(HashSet<string> codes)
    {
        _codes = codes;
        Codes = PermissionCatalog.Codes.Where(_codes.Contains).ToArray();
    }

    /// <summary>The held codes, in catalog order.</summary>
    public IReadOnlyList<string> Codes { get; }

    public bool Has(string code) => _codes.Contains(code);

    /// <summary>The role's grants restricted to codes the catalog knows: a retired or misspelt code grants nothing.</summary>
    public static EffectivePermissions Compute(IEnumerable<string> roleGrants) =>
        new(roleGrants.Where(PermissionCatalog.IsKnown).ToHashSet(StringComparer.Ordinal));
}

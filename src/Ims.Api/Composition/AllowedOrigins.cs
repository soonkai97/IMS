namespace Ims.Api;

/// <summary>The browser origins allowed to call the API (<c>Cors:Origins</c>). One list feeds both the CORS policy and the CSRF origin check.</summary>
public sealed class AllowedOrigins(IEnumerable<string> origins)
{
    private readonly HashSet<string> _origins = new(origins.Select(Normalise), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> All => _origins;

    public bool Contains(string origin) => !string.IsNullOrEmpty(origin) && _origins.Contains(Normalise(origin));

    private static string Normalise(string origin) => origin.Trim().TrimEnd('/');
}

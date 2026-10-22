using Ims.Application;
using Ims.Domain;
namespace Ims.Api;

/// <summary>Reads the <c>If-Match</c> header: the concurrency token the client last saw.</summary>
public static class IfMatchHeader
{
    /// <summary>
    /// The token, with or without the quotes (an unquoted token is still a token, never a silently skipped check).
    /// When the token is optional, a missing header and <c>*</c> (the explicit "any version" opt-out) both yield null, which the handler reads as "no check".
    /// When it is <paramref name="required"/>, a missing header, an empty one and <c>*</c> are all <c>428</c>: a client cannot overwrite someone else's change by forgetting the token or by opting out of it.
    /// </summary>
    /// <exception cref="PreconditionRequiredException">The header is required and missing, empty or <c>*</c>.</exception>
    public static string? Read(HttpRequest request, bool required)
    {
        var raw = request.Headers[Microsoft.Net.Http.Headers.HeaderNames.IfMatch].ToString();
        if (string.IsNullOrWhiteSpace(raw)) return required ? throw new PreconditionRequiredException(Messages.IfMatchRequired) : null;
        var first = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (first == "*") return required ? throw new PreconditionRequiredException(Messages.IfMatchRequired) : null;
        // An empty value (no entry at all, or just quotes) names no version: it is a missing header, not a skipped check.
        var token = (first.StartsWith("W/", StringComparison.Ordinal) ? first[2..] : first).Trim('"').Trim();
        return token.Length == 0 ? throw new PreconditionRequiredException(Messages.IfMatchRequired) : token;
    }
}

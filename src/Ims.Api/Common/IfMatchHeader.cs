using Ims.Application;
using Ims.Domain;
namespace Ims.Api;

/// <summary>Reads the <c>If-Match</c> header: the concurrency token the client last saw.</summary>
public static class IfMatchHeader
{
    /// <summary>
    /// The token, with or without the quotes (an unquoted token is still a token, never a silently skipped check). <c>*</c> is the explicit "any version" opt-out and yields null.
    /// A missing header yields null when <paramref name="required"/> is false and is <c>428</c> when it is true, so a client cannot overwrite someone else's change by forgetting it.
    /// </summary>
    /// <exception cref="PreconditionRequiredException">The header is required and missing or empty.</exception>
    public static string? Read(HttpRequest request, bool required)
    {
        var raw = request.Headers[Microsoft.Net.Http.Headers.HeaderNames.IfMatch].ToString();
        if (string.IsNullOrWhiteSpace(raw)) return required ? throw new PreconditionRequiredException(Messages.IfMatchRequired) : null;
        var first = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (first == "*") return null;
        // An empty value (no entry at all, or just quotes) names no version: it is a missing header, not a skipped check.
        var token = (first.StartsWith("W/", StringComparison.Ordinal) ? first[2..] : first).Trim('"').Trim();
        return token.Length == 0 ? throw new PreconditionRequiredException(Messages.IfMatchRequired) : token;
    }
}

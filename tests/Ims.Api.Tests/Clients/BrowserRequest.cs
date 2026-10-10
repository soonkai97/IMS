using Ims.Api.Features.Auth;

namespace Ims.Api.Tests.Clients;

/// <summary>The headers a real browser adds to a cookie-carrying call. <see cref="Trusted"/> is what our own web app sends.</summary>
public sealed record BrowserRequest(string? Origin, string? Referer, string? RequestedWith)
{
    public const string AllowedOrigin = "http://localhost:4200"; // Cors:Origins in appsettings.json

    public static BrowserRequest Trusted { get; } = new(AllowedOrigin, null, AuthCookie.CsrfHeaderValue);

    public BrowserRequest WithOrigin(string? origin) => this with { Origin = origin };
    public BrowserRequest WithReferer(string? referer) => this with { Referer = referer };
    public BrowserRequest WithRequestedWith(string? value) => this with { RequestedWith = value };
}

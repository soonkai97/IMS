using Ims.Application;
using Ims.Domain;

namespace Ims.Api.Features.Auth;

/// <summary>
/// Three layers, any one of which stops a forged cross-site request: SameSite=Strict on the cookie (the browser does not send it),
/// the required custom header (a form post cannot set it; a cross-origin script needs a CORS preflight that only the allowed origins
/// pass) and this check of Origin/Referer against the same list the CORS policy uses. A request with neither Origin nor Referer is
/// refused: browsers always send one of them on a cookie-carrying POST.
/// </summary>
public sealed class CsrfGuard(AllowedOrigins allowed) : ICsrfGuard
{
    public void EnsureTrusted(HttpContext context)
    {
        var headers = context.Request.Headers;
        if (headers[AuthCookie.CsrfHeaderName] != AuthCookie.CsrfHeaderValue) throw Refused();

        var origin = headers[HeaderNames.Origin].ToString();
        if (string.IsNullOrEmpty(origin)) origin = OriginOf(headers[HeaderNames.Referer].ToString());
        if (!allowed.Contains(origin)) throw Refused();
    }

    /// <summary>"https://app.example.com/some/page?x=1" becomes "https://app.example.com"; anything that is not an absolute http(s) URL becomes "".</summary>
    private static string OriginOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.GetLeftPart(UriPartial.Authority) : "";

    private static ForbiddenException Refused() => new(Messages.CrossSiteRequestRefused);
}

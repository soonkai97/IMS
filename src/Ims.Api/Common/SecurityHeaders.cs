namespace Ims.Api;

/// <summary>
/// The response headers every API response carries. The API answers JSON and downloads, never pages, so the policies are the strictest
/// that work: nothing may be loaded, framed or embedded from an API response. The static web app has its own, looser set
/// (docs/security/web-headers.md).
/// </summary>
public static class SecurityHeaders
{
    public const string NoSniff = "nosniff";
    public const string ReferrerPolicy = "no-referrer";

    /// <summary>"same-site": the web app may live on a sibling origin (another port or subdomain) but no other site may embed API responses.</summary>
    public const string CrossOriginResourcePolicy = "same-site";

    public const string FrameOptions = "DENY";

    /// <summary>No content may be loaded or framed from a JSON response, and it cannot be the target of a form or a base URL.</summary>
    public const string ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    /// <summary>Every powerful browser feature switched off for API responses.</summary>
    public const string PermissionsPolicy =
        "accelerometer=(), autoplay=(), camera=(), display-capture=(), encrypted-media=(), fullscreen=(), geolocation=(), gyroscope=(), " +
        "magnetometer=(), microphone=(), midi=(), payment=(), picture-in-picture=(), publickey-credentials-get=(), usb=(), xr-spatial-tracking=()";

    /// <summary>Responses of the sign-in endpoints hold tokens and must never be stored by a browser or a proxy.</summary>
    public const string NoStore = "no-store";

    public static readonly TimeSpan HstsMaxAge = TimeSpan.FromDays(365);
}

namespace Ims.Api;

/// <summary>Transport-level hardening: the security response headers and HSTS.</summary>
public static class SecurityHeadersSetup
{
    /// <summary>One year, subdomains included (the checklist in docs/security). HSTS is only sent over HTTPS, so it is inert on plain HTTP.</summary>
    public static IServiceCollection AddTransportSecurity(this IServiceCollection services)
    {
        services.AddHsts(o =>
        {
            o.MaxAge = SecurityHeaders.HstsMaxAge;
            o.IncludeSubDomains = true;
        });
        return services;
    }

    /// <summary>Must run first among the middleware that can write a response, so every status (200, 4xx, 5xx, 429) is covered.</summary>
    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        app.UseMiddleware<SecurityHeadersMiddleware>();
        return app;
    }
}

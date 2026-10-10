namespace Ims.Api;

/// <summary>
/// Adds the security headers to every response, including errors produced by the exception handler, the status-code pages, the rate
/// limiter and the authentication middleware. The headers are added when the response starts, and only if the endpoint did not set the
/// same header itself (attachment downloads carry their own stricter Content-Security-Policy and Cache-Control).
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private static readonly PathString AuthPath = "/" + ApiRoutes.Auth;
    private static readonly PathString SwaggerPath = "/" + OpenApiConstants.UiRoute;

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            Apply((HttpContext)state);
            return Task.CompletedTask;
        }, context);
        return next(context);
    }

    private static void Apply(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.TryAdd(HeaderNames.ContentTypeOptions, SecurityHeaders.NoSniff);
        headers.TryAdd(HeaderNames.ReferrerPolicy, SecurityHeaders.ReferrerPolicy);
        headers.TryAdd(HeaderNames.CrossOriginResourcePolicy, SecurityHeaders.CrossOriginResourcePolicy);
        headers.TryAdd(HeaderNames.PermissionsPolicy, SecurityHeaders.PermissionsPolicy);
        headers.TryAdd(HeaderNames.FrameOptions, SecurityHeaders.FrameOptions);

        // Swagger UI (Development and Testing only) is an HTML page with scripts of its own, so the JSON-only policy would blank it.
        if (!context.Request.Path.StartsWithSegments(SwaggerPath)) headers.TryAdd(HeaderNames.ContentSecurityPolicy, SecurityHeaders.ContentSecurityPolicy);
        if (context.Request.Path.StartsWithSegments(AuthPath)) headers.TryAdd(HeaderNames.CacheControl, SecurityHeaders.NoStore);
    }
}

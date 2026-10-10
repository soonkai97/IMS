using Ims.Domain;
using Serilog;
using Serilog.Events;

namespace Ims.Api;

public static class PipelineSetup
{
    /// <summary>
    /// Middleware order matters: forwarded headers first (so the real client address is known), then the correlation id (every later log
    /// line carries it); request logging outside the exception handler (it sees the mapped status); authentication before the rate
    /// limiter (so the per-user bucket applies) and before authorization; request timeouts last, right before the endpoint.
    /// </summary>
    public static WebApplication UsePresentation(this WebApplication app)
    {
        if (app.Configuration.GetValue(ApiConfigKeys.ForwardedHeadersEnabled, false)) app.UseForwardedHeaders();

        // HSTS only matters over HTTPS (TLS is terminated by the reverse proxy); it is harmless on plain HTTP and absent in Development.
        if (!app.Environment.IsDevelopment()) app.UseHsts();

        app.UseSecurityHeaders();

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging(o =>
        {
            o.MessageTemplate = LogTemplates.Request;
            o.GetLevel = (ctx, _, ex) =>
                ex is not null || ctx.Response.StatusCode >= StatusCodes.Status500InternalServerError ? LogEventLevel.Error
                : ctx.Response.StatusCode >= StatusCodes.Status400BadRequest ? LogEventLevel.Warning
                : ctx.Request.Path.StartsWithSegments(ApiRoutes.Health) ? LogEventLevel.Verbose
                : LogEventLevel.Information;
            o.EnrichDiagnosticContext = (diag, ctx) =>
            {
                diag.Set("User", ctx.User.Identity?.Name ?? AuditEntities.Anonymous);
                diag.Set("ClientIp", ctx.Connection.RemoteIpAddress?.ToString());
            };
        });
        app.UseExceptionHandler();
        app.UseStatusCodePages(ProblemResponses.WriteStatusCodeAsync);
        app.UseCors();

        // Swagger UI is plain middleware, so it must sit before authentication: the global fallback policy would otherwise answer 401.
        var docsEnabled = app.Environment.IsDevelopment() || app.Environment.IsEnvironment(EnvironmentNames.Testing);
        if (docsEnabled) app.UseSwaggerUi();

        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.UseRequestTimeouts();

        if (docsEnabled) app.MapOpenApi().AllowAnonymous();
        app.MapControllers();
        app.MapHealthChecks(ApiRoutes.Health).AllowAnonymous();
        return app;
    }
}

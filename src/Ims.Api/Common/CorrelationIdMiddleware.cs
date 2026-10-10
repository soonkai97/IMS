using Serilog.Context;
namespace Ims.Api;

/// <summary>Accepts or creates X-Correlation-Id, echoes it on the response and attaches it to every log line of the request.</summary>
public class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var id = ctx.Request.Headers[HeaderNames.CorrelationId].FirstOrDefault();
        // Caller-supplied ids are accepted only if short and plain, so they cannot be used to forge log lines.
        if (string.IsNullOrWhiteSpace(id) || id.Length > CorrelationRules.MaxLength || !id.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
            id = Guid.NewGuid().ToString("N");
        ctx.Response.Headers[HeaderNames.CorrelationId] = id;
        ctx.TraceIdentifier = id;
        using (LogContext.PushProperty(LogTemplates.CorrelationProperty, id))
            await next(ctx);
    }
}

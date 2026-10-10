using Ims.Domain;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace Ims.Api;

/// <summary>
/// Request timeouts. A request that runs too long is cancelled through its cancellation token (which also cancels the running SQL
/// command) and answered 504 with a ProblemDetails body. Normal requests get 30 s; exports and imports opt in to a longer policy
/// with [RequestTimeout(RequestTimeoutPolicies.Long)]. The database command timeout is longer than the default on purpose, so the
/// HTTP timeout is the one clients see.
/// </summary>
public static class RequestTimeoutSetup
{
    public static IServiceCollection AddRequestTimeoutPolicies(this IServiceCollection services)
    {
        services.AddRequestTimeouts(o =>
        {
            o.DefaultPolicy = Policy(Limits.RequestTimeoutSeconds);
            o.AddPolicy(RequestTimeoutPolicies.Long, Policy(Limits.LongRequestTimeoutSeconds));
        });
        return services;
    }

    private static RequestTimeoutPolicy Policy(int seconds) => new()
    {
        Timeout = TimeSpan.FromSeconds(seconds),
        TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
        WriteTimeoutResponse = ctx => WriteTimeoutAsync(ctx)
    };

    private static async Task WriteTimeoutAsync(HttpContext ctx)
    {
        ctx.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
        ctx.Response.ContentType = "application/problem+json";
        var pd = new ProblemDetails
        {
            Status = StatusCodes.Status504GatewayTimeout, Title = Messages.RequestTimedOutTitle, Detail = Messages.RequestTimedOut
        };
        pd.Extensions[ProblemExtensions.CorrelationId] = ctx.TraceIdentifier;
        await ctx.Response.WriteAsJsonAsync(pd, ctx.RequestAborted);
    }
}

using Ims.Application;
using Ims.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Timeouts;

namespace Ims.Api;

/// <summary>
/// Maps exceptions to ProblemDetails. Application and domain exceptions are mapped here; provider exceptions (for example SQL timeouts)
/// are translated by the <see cref="IExceptionMapper"/>s the infrastructure registers. Unexpected errors are logged with their stack and
/// returned as an opaque 500 with a reference. A client that disconnects is not an error.
/// </summary>
public class ApiExceptionHandler(ILogger<ApiExceptionHandler> log, IProblemDetailsService problems, IEnumerable<IExceptionMapper> mappers) : IExceptionHandler
{
    private const int ClientClosedRequest = 499;

    private sealed record Outcome(int Status, string Title, string Detail, bool Expected);

    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is OperationCanceledException) return HandleCancellation(ctx);

        var outcome = Classify(ex, ctx);
        if (outcome.Expected)
            log.LogInformation("Request rejected with {Status}: {Reason} ({ExceptionType})", outcome.Status, outcome.Detail, ex.GetType().Name);
        else
            log.LogError(ex, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);

        ctx.Response.StatusCode = outcome.Status;
        ProblemResponses.StampCorrelationId(ctx);
        var pd = ProblemResponses.Create(ctx, outcome.Status, outcome.Title, outcome.Detail);
        switch (ex)
        {
            case InsufficientStockException ise:
                pd.Extensions[ProblemExtensions.ProductId] = ise.ProductId;
                pd.Extensions[ProblemExtensions.Available] = ise.Available;
                pd.Extensions[ProblemExtensions.Requested] = ise.Requested;
                break;
            case ValidationException { Errors.Count: > 0 } ve:
                pd.Extensions[ProblemExtensions.Errors] = ve.Errors;
                break;
        }
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = ctx, ProblemDetails = pd, Exception = ex });
    }

    private Outcome Classify(Exception ex, HttpContext ctx)
    {
        switch (ex)
        {
            case UnauthorizedException: return Expected(StatusCodes.Status401Unauthorized, Messages.UnauthorizedTitle, ex);
            case ForbiddenException: return Expected(StatusCodes.Status403Forbidden, Messages.ForbiddenTitle, ex);
            case NotFoundException: return Expected(StatusCodes.Status404NotFound, Messages.NotFoundTitle, ex);
            case ConflictException: return Expected(StatusCodes.Status409Conflict, Messages.ConflictTitle, ex);
            case InsufficientStockException: return Expected(StatusCodes.Status409Conflict, Messages.InsufficientStockTitle, ex);
            case ConcurrencyException:
                // The message chain of a concurrency exception carries internals; the client gets the plain sentence.
                return new Outcome(StatusCodes.Status409Conflict, Messages.ConflictTitle, Messages.ConcurrencyConflict, true);
            case ValidationException or DomainException: return Expected(StatusCodes.Status400BadRequest, Messages.RequestRejectedTitle, ex);
            case BadHttpRequestException: return Expected(StatusCodes.Status400BadRequest, Messages.BadRequestTitle, ex);
            case TimeoutException:
                return new Outcome(StatusCodes.Status504GatewayTimeout, Messages.RequestTimedOutTitle, Messages.RequestTimedOut, true);
        }

        foreach (var mapper in mappers)
            if (mapper.Map(ex) is { } m) return new Outcome(m.Status, m.Title, m.Detail, true);

        return new Outcome(StatusCodes.Status500InternalServerError, Messages.ServerErrorTitle, Messages.ServerError(ctx.TraceIdentifier), false);
    }

    private static Outcome Expected(int status, string title, Exception ex) => new(status, title, ex.Message, true);

    /// <summary>
    /// A cancelled request is either the client going away (nothing to answer, not an error) or the request timeout firing
    /// (the timeout middleware normally answers that itself; if it did not, answer 504 here).
    /// </summary>
    private bool HandleCancellation(HttpContext ctx)
    {
        var timedOut = ctx.Features.Get<IHttpRequestTimeoutFeature>()?.RequestTimeoutToken.IsCancellationRequested == true;
        if (timedOut)
        {
            log.LogWarning("Request {Method} {Path} timed out", ctx.Request.Method, ctx.Request.Path);
            ctx.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            return true;
        }
        log.LogDebug("Request {Method} {Path} was cancelled by the client", ctx.Request.Method, ctx.Request.Path);
        ctx.Response.StatusCode = ClientClosedRequest;
        return true;
    }
}

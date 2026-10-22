using Ims.Application;
using Ims.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Ims.Api;

/// <summary>
/// One shape for every error the API returns, including the ones produced by the framework itself (bad model binding, 401/403 from the
/// authentication middleware, unmatched routes): RFC 9457 ProblemDetails with a correlationId, and the X-Correlation-Id header.
/// </summary>
public static class ProblemResponses
{
    public const string ProblemJson = "application/problem+json";
    private const string JsonPathRoot = "$";

    public static ProblemDetails Create(HttpContext ctx, int status, string title, string detail)
    {
        var pd = new ProblemDetails { Status = status, Title = title, Detail = detail };
        pd.Extensions[ProblemExtensions.CorrelationId] = ctx.TraceIdentifier;
        return pd;
    }

    /// <summary>The exception handler clears response headers, so the correlation id is set again on every error response.</summary>
    public static void StampCorrelationId(HttpContext ctx) => ctx.Response.Headers[HeaderNames.CorrelationId] = ctx.TraceIdentifier;

    /// <summary>Invalid model state (malformed JSON, wrong types, missing body) becomes a 400 ProblemDetails with camelCase field errors.</summary>
    public static IActionResult FromModelState(ActionContext context)
    {
        var http = context.HttpContext;
        var errors = context.ModelState
            .Where(kv => kv.Value is { Errors.Count: > 0 })
            .ToDictionary(kv => ErrorKeys.Normalize(kv.Key),
                kv => kv.Value!.Errors.Select(e => SafeMessage(kv.Key, e)).Distinct().ToArray());

        // Framework messages for malformed JSON describe internals; clients get a plain sentence plus the field list.
        var pd = Create(http, StatusCodes.Status400BadRequest, Messages.RequestRejectedTitle, Messages.RequestNotValid);
        if (errors.Count > 0) pd.Extensions[ProblemExtensions.Errors] = errors;
        StampCorrelationId(http);
        return new ObjectResult(pd) { StatusCode = StatusCodes.Status400BadRequest, ContentTypes = { ProblemJson } };
    }

    /// <summary>
    /// The JSON formatter reports a body it cannot read under a JSON path key ("$", "$.lines") and puts the serializer's own message in it: CLR type names,
    /// line and byte positions. That text, and any error that carries an exception or no message, is never sent to a client; the field key still is.
    /// A message from the binder for a route or query value is a plain sentence and is returned as is.
    /// </summary>
    private static string SafeMessage(string key, ModelError error) =>
        key.StartsWith(JsonPathRoot, StringComparison.Ordinal) || error.Exception is not null || string.IsNullOrWhiteSpace(error.ErrorMessage) ? Messages.RequestNotValid : error.ErrorMessage;

    /// <summary>
    /// A request refused by the rate limiter: 429 with a readable sentence, the <c>Retry-After</c> header and <c>retryAfterSeconds</c>, so a client can
    /// say how long to wait instead of "the request could not be processed".
    /// </summary>
    public static async Task WriteRateLimitedAsync(HttpContext http, TimeSpan? retryAfter, CancellationToken ct)
    {
        var seconds = (int)Math.Max(1, Math.Ceiling((retryAfter ?? TimeSpan.FromMinutes(Limits.RateLimitWindowMinutes)).TotalSeconds));
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers[Microsoft.Net.Http.Headers.HeaderNames.RetryAfter] = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        StampCorrelationId(http);
        var pd = Create(http, StatusCodes.Status429TooManyRequests, Messages.TooManyRequestsTitle, Messages.TooManyRequests(seconds));
        pd.Extensions[ProblemExtensions.Code] = ProblemCodes.RateLimited;
        pd.Extensions[ProblemExtensions.RetryAfterSeconds] = seconds;
        http.Response.ContentType = ProblemJson;
        await http.Response.WriteAsJsonAsync(pd, (System.Text.Json.JsonSerializerOptions?)null, ProblemJson, ct);
    }

    /// <summary>401, 403, 404 (no route) and similar statuses that leave the body empty get a ProblemDetails body.</summary>
    public static async Task WriteStatusCodeAsync(StatusCodeContext context)
    {
        var http = context.HttpContext;
        var status = http.Response.StatusCode;
        var (title, detail) = status switch
        {
            StatusCodes.Status401Unauthorized => (Messages.UnauthorizedTitle, Messages.SignInRequired),
            StatusCodes.Status403Forbidden => (Messages.ForbiddenTitle, Messages.NotAllowed),
            StatusCodes.Status404NotFound => (Messages.NotFoundTitle, Messages.RouteNotFound),
            _ => (Messages.RequestRejectedTitle, Messages.RequestRejectedGeneric)
        };
        StampCorrelationId(http);
        http.Response.ContentType = ProblemJson;
        await http.Response.WriteAsJsonAsync(Create(http, status, title, detail), (System.Text.Json.JsonSerializerOptions?)null, ProblemJson, http.RequestAborted);
    }
}

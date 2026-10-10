using Ims.Application;
using Ims.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Ims.Api;

/// <summary>
/// One shape for every error the API returns, including the ones produced by the framework itself (bad model binding, 401/403 from the
/// authentication middleware, unmatched routes): RFC 9457 ProblemDetails with a correlationId, and the X-Correlation-Id header.
/// </summary>
public static class ProblemResponses
{
    public const string ProblemJson = "application/problem+json";

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
                kv => kv.Value!.Errors.Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? Messages.RequestNotValid : e.ErrorMessage).Distinct().ToArray());

        // Framework messages for malformed JSON describe internals; clients get a plain sentence plus the field list.
        var pd = Create(http, StatusCodes.Status400BadRequest, Messages.RequestRejectedTitle, Messages.RequestNotValid);
        if (errors.Count > 0) pd.Extensions[ProblemExtensions.Errors] = errors;
        StampCorrelationId(http);
        return new ObjectResult(pd) { StatusCode = StatusCodes.Status400BadRequest, ContentTypes = { ProblemJson } };
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

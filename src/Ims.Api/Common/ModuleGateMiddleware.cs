using Ims.Application.Features.Modules;
using Microsoft.AspNetCore.Authorization;
namespace Ims.Api;

/// <summary>
/// The gate of the modules (ADR modules-and-tracking-dimensions 4.3). It sits between authentication and authorization on purpose: an MVC filter would run after the policy and
/// give 403 to a person without the permission but 404 to one with it, which lets a caller tell that a module exists. Here a caller with no valid token goes on to authorization
/// (401, as ever); a signed-in caller of an endpoint of an off module gets the 404 of an unknown route, whatever they may do; an endpoint of an enabled module goes on to the policy
/// (403 or 200). Endpoints without <see cref="RequiresModuleAttribute"/> are never touched and cost nothing.
/// </summary>
public class ModuleGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, IModuleSwitches switches)
    {
        var required = http.GetEndpoint()?.Metadata.GetOrderedMetadata<RequiresModuleAttribute>();
        // A caller with no valid token goes on to authorization (401), except on an endpoint that allows anonymous callers: there authorization would let them in, so the gate must decide.
        var anonymousAllowed = http.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
        if (required is not { Count: > 0 } || (http.User.Identity?.IsAuthenticated != true && !anonymousAllowed))
        {
            await next(http);
            return;
        }

        foreach (var module in required)
        {
            if (await switches.IsEnabledAsync(module.Code, http.RequestAborted)) continue;
            // An empty 404: the status-code pages write the same ProblemDetails as for a route that does not exist.
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next(http);
    }
}

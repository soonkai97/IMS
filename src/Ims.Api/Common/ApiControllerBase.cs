using Ims.Application;
using Microsoft.AspNetCore.Mvc;
namespace Ims.Api;

/// <summary>Base for every controller. Controllers are thin: they translate HTTP to a command or query and send it.</summary>
[ApiController]
public abstract class ApiControllerBase(IDispatcher dispatcher) : ControllerBase
{
    protected Task<T> Send<T>(IRequest<T> request) => dispatcher.SendAsync(request, HttpContext.RequestAborted);

    protected async Task<IActionResult> SendOk<T>(IRequest<T> request) => Ok(await Send(request));
}

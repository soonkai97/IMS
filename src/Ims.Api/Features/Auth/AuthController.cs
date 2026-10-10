using Ims.Application;
using Ims.Application.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Ims.Api.Features.Auth;

[Route(ApiRoutes.Auth)]
public class AuthController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [HttpPost(RouteSegments.Login), AllowAnonymous, EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<AuthResult>(StatusCodes.Status200OK)]
    public Task<IActionResult> Login([FromBody] LoginRequest body) => SendOk(new LoginCommand(body));

    [HttpPost(RouteSegments.Refresh), AllowAnonymous, EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<AuthResult>(StatusCodes.Status200OK)]
    public Task<IActionResult> Refresh([FromBody] RefreshRequest body) => SendOk(new RefreshTokenCommand(body));

    [HttpPost(RouteSegments.Logout), AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest body)
    {
        await Send(new LogoutCommand(body));
        return NoContent();
    }

    /// <summary>Changes the signed-in user's own password and ends all of their sessions.</summary>
    [HttpPost(RouteSegments.ChangePassword)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest body)
    {
        await Send(new ChangePasswordCommand(body));
        return NoContent();
    }

    [HttpGet(RouteSegments.Me)]
    [ProducesResponseType<UserInfo>(StatusCodes.Status200OK)]
    public Task<IActionResult> Me() => SendOk(new GetCurrentUserQuery());
}

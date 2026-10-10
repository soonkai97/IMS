using Ims.Application;
using Ims.Application.Features.Auth;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Ims.Api.Features.Auth;

[Route(ApiRoutes.Auth)]
public class AuthController(IDispatcher dispatcher, IRefreshTokenCookie refreshCookie, ICsrfGuard csrf, IOptions<AuthOptions> options)
    : ApiControllerBase(dispatcher)
{
    [HttpPost(RouteSegments.Login), AllowAnonymous, EnableRateLimiting(RateLimitPolicies.Login)]
    [EndpointDescription(AuthDocs.Login)]
    [ProducesResponseType<AuthResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] LoginRequest body) => Issued(await Send(new LoginCommand(body)));

    [HttpPost(RouteSegments.Refresh), AllowAnonymous, EnableRateLimiting(RateLimitPolicies.Login)]
    [EndpointDescription(AuthDocs.Refresh)]
    [ProducesResponseType<AuthResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest? body)
    {
        var (token, fromCookie) = Presented(body);
        if (token is null) throw new UnauthorizedException(Messages.SessionExpired); // no cookie and no body token: nobody is signed in
        try
        {
            return Issued(await Send(new RefreshTokenCommand(new RefreshRequest(token))));
        }
        catch (UnauthorizedException) when (fromCookie)
        {
            // The cookie holds a token that is expired, revoked or already rotated: drop it so the browser stops sending it.
            // Registered on the response start because the exception handler resets the headers set so far.
            Response.OnStarting(() =>
            {
                refreshCookie.Clear(HttpContext);
                return Task.CompletedTask;
            });
            throw;
        }
    }

    [HttpPost(RouteSegments.Logout), AllowAnonymous]
    [EndpointDescription(AuthDocs.Logout)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest? body)
    {
        var (token, _) = Presented(body);
        await Send(new LogoutCommand(new RefreshRequest(token ?? "")));
        refreshCookie.Clear(HttpContext);
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

    /// <summary>
    /// Sets the refresh cookie, and returns the result with the refresh token in the body only while the (deprecated) body delivery is on.
    /// </summary>
    private OkObjectResult Issued(AuthResult result)
    {
        refreshCookie.Write(HttpContext, result.RefreshToken!);
        return Ok(options.Value.ReturnRefreshTokenInBody ? result : result with { RefreshToken = null });
    }

    /// <summary>
    /// The refresh token the caller presents: the request body wins (an explicit credential, nothing for a forged request to ride on);
    /// otherwise the cookie, which the browser attaches by itself and therefore must pass the CSRF check. An explicitly empty token in
    /// the body is passed on as empty so the command's validator answers 400, as it always did. Null when nothing was presented.
    /// </summary>
    private (string? Token, bool FromCookie) Presented(RefreshRequest? body)
    {
        if (!string.IsNullOrEmpty(body?.RefreshToken)) return (body.RefreshToken, false);
        var cookie = refreshCookie.Read(HttpContext);
        if (cookie is not null)
        {
            csrf.EnsureTrusted(HttpContext);
            return (cookie, true);
        }
        return (body?.RefreshToken, false);
    }
}

using System.Security.Claims;
using Ims.Application;
using Ims.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
namespace Ims.Api;

/// <summary>
/// The second half of token validation (ADR roles-permissions 3.2). The signature, issuer, audience and lifetime say the token is genuine; this says who the person
/// is now. The token is read for the user id and the name only: nothing in it decides rights, so an old token, or one carrying a role claim, gains nothing.
/// A user who is missing or inactive has no snapshot and fails here, which is the 401 an expired token gets, so the web's refresh then sign-in path runs.
/// </summary>
public sealed class AccessSnapshotBearerEvents : JwtBearerEvents
{
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var token = context.Principal;
        if (token?.Identity is not ClaimsIdentity original || !int.TryParse(token.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            context.Fail(Messages.SessionExpired);
            return;
        }

        var access = await context.HttpContext.RequestServices.GetRequiredService<IAccessSnapshots>().GetAsync(userId, context.HttpContext.RequestAborted);
        if (access is null)
        {
            context.Fail(Messages.SessionExpired);
            return;
        }

        context.HttpContext.Items[HttpItemKeys.AccessSnapshot] = access;
        // A new identity from a short list, not the token's own claims minus some: whatever else the token carries is dropped.
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Role, access.RoleCode) };
        if (original.Name is { } name) claims.Add(new Claim(original.NameClaimType, name));
        context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, original.AuthenticationType, original.NameClaimType, original.RoleClaimType));
    }
}

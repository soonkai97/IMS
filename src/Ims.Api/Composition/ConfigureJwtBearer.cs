using System.Globalization;
using System.Security.Claims;
using Ims.Application;
using Ims.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Ims.Api;

public sealed class ConfigureJwtBearer(ITokenValidationSettings settings) : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions o)
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = settings.Issuer,
            ValidateAudience = true, ValidAudience = settings.Audience,
            ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(settings.SigningKey),
            ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(Limits.ClockSkewSeconds),
            RoleClaimType = ClaimTypes.Role, NameClaimType = ClaimTypes.Name
        };
        o.Events = new JwtBearerEvents { OnTokenValidated = RequireCurrentClaimsVersion };
    }

    public void Configure(JwtBearerOptions options) => Configure(JwtBearerDefaults.AuthenticationScheme, options);

    /// <summary>
    /// A token written for another shape of the claims (none at all, or an older or newer version) is refused with 401, so a release that changes the claims never
    /// honours tokens of the old shape; the web refreshes on a 401 and gets a token of the current shape.
    /// </summary>
    private static Task RequireCurrentClaimsVersion(TokenValidatedContext context)
    {
        var claim = context.Principal?.FindFirstValue(ClaimNames.ClaimsVersion);
        if (!int.TryParse(claim, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version != ClaimNames.CurrentClaimsVersion)
            context.Fail("The access token has another version of the claims.");
        return Task.CompletedTask;
    }
}

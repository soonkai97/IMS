using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Ims.Api.Tests.Support;

/// <summary>Makes access tokens the way the API does, but with chosen faults (expired, wrong key, wrong audience).</summary>
public static class ForgedTokens
{
    public const string Issuer = "ims";
    public const string Audience = "ims-clients";

    public static string Create(string signingKey, string role = "Admin", DateTime? expiresAt = null, string issuer = Issuer, string audience = Audience)
    {
        var expires = expiresAt ?? DateTime.UtcNow.AddMinutes(10);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Name, "forged"),
            new Claim(ClaimTypes.Role, role)
        };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);
        // notBefore must be earlier than expires, so an already-expired token starts an hour before it ends.
        var token = new JwtSecurityToken(issuer, audience, claims, notBefore: expires.AddHours(-1), expires: expires, signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

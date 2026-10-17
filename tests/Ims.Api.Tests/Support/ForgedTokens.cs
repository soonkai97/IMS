using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Ims.Domain;
using Microsoft.IdentityModel.Tokens;

namespace Ims.Api.Tests.Support;

/// <summary>Makes access tokens the way the API does, but with chosen faults (expired, wrong key, wrong audience, no or another claims version).</summary>
public static class ForgedTokens
{
    public const string Issuer = "ims";
    public const string Audience = "ims-clients";

    /// <param name="allWarehouses">The <c>allwh</c> claim; <paramref name="warehouseIds"/> are the <c>wh</c> claims (internal ids) of a token that does not see every warehouse.</param>
    /// <param name="claimsVersion">The <c>acv</c> claim; the version the API accepts unless told otherwise, <c>null</c> for a token that has none (as one issued before the claim existed).</param>
    public static string Create(string signingKey, string role = "Admin", DateTime? expiresAt = null, string issuer = Issuer, string audience = Audience,
        IEnumerable<string>? permissions = null, int? claimsVersion = ClaimNames.CurrentClaimsVersion, int userId = 1, bool allWarehouses = true,
        IEnumerable<int>? warehouseIds = null)
    {
        var expires = expiresAt ?? DateTime.UtcNow.AddMinutes(10);
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, "forged"),
            new Claim(ClaimTypes.Role, role),
            new Claim(ClaimNames.AllWarehouses, allWarehouses ? ClaimNames.True : ClaimNames.False)
        };
        claims.AddRange((warehouseIds ?? []).Select(w => new Claim(ClaimNames.Warehouse, w.ToString())));
        if (claimsVersion is { } version) claims.Add(new Claim(ClaimNames.ClaimsVersion, version.ToString()));
        claims.AddRange((permissions ?? []).Select(p => new Claim(ClaimNames.Permission, p)));
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);
        // notBefore must be earlier than expires, so an already-expired token starts an hour before it ends.
        var token = new JwtSecurityToken(issuer, audience, claims, notBefore: expires.AddHours(-1), expires: expires, signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

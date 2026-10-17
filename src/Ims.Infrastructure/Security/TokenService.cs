using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Ims.Application;
using Ims.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
namespace Ims.Infrastructure.Security;

public class TokenService(IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    private readonly JwtOptions _o = options.Value;

    /// <summary>
    /// Identity (sub, name) and rights from one snapshot: the role code (display and audit), one <c>perm</c> claim per permission held, <c>allwh</c>, the <c>wh</c> list
    /// only for a user who does not see every warehouse, and the claim-shape version. Rights are as old as the token, which is why it lives minutes (ADR roles-permissions 3.2).
    /// </summary>
    public string CreateAccessToken(string username, AccessSnapshot access, out DateTime expiresAt)
    {
        expiresAt = clock.UtcNow.AddMinutes(_o.AccessMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, access.UserId.ToString()),
            new(ClaimTypes.NameIdentifier, access.UserId.ToString()),
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, access.RoleCode),
            new(ClaimNames.AllWarehouses, access.AllWarehouses ? "true" : "false"),
            new(ClaimNames.ClaimsVersion, ClaimNames.CurrentClaimsVersion.ToString(CultureInfo.InvariantCulture)),
        };
        claims.AddRange(access.Permissions.Codes.Select(code => new Claim(ClaimNames.Permission, code)));
        if (!access.AllWarehouses) claims.AddRange(access.WarehouseIds.Select(id => new Claim(ClaimNames.Warehouse, id.ToString(CultureInfo.InvariantCulture))));

        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_o.Issuer, _o.Audience, claims, notBefore: clock.UtcNow, expires: expiresAt, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateRefreshTokenValue() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(Limits.RefreshTokenBytes));

    public string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

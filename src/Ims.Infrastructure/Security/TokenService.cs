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

    /// <summary>Identity only (ADR roles-permissions 3.2): who the person is, never what they may do. Rights are read per request from the access snapshot, so a token cannot outlive a demotion.</summary>
    public string CreateAccessToken(User user, out DateTime expiresAt)
    {
        expiresAt = clock.UtcNow.AddMinutes(_o.AccessMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
        };

        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.Key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_o.Issuer, _o.Audience, claims, notBefore: clock.UtcNow, expires: expiresAt, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateRefreshTokenValue() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(Limits.RefreshTokenBytes));

    public string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

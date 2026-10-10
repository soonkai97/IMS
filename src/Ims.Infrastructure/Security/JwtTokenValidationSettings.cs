using System.Text;
using Ims.Application;
using Microsoft.Extensions.Options;

namespace Ims.Infrastructure.Security;

public sealed class JwtTokenValidationSettings(IOptions<JwtOptions> options) : ITokenValidationSettings
{
    private readonly JwtOptions _o = options.Value;

    public string Issuer => _o.Issuer;
    public string Audience => _o.Audience;
    public byte[] SigningKey => Encoding.UTF8.GetBytes(_o.Key);
}

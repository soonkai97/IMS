using System.ComponentModel.DataAnnotations;
using Ims.Domain;
namespace Ims.Infrastructure.Security;

/// <summary>Bound from the "Jwt" section and validated at startup, so a bad configuration stops the app instead of failing on first login.</summary>
public class JwtOptions
{
    [Required, MinLength(Limits.JwtKeyMinLength)] public string Key { get; set; } = "";
    [Required] public string Issuer { get; set; } = "";
    [Required] public string Audience { get; set; } = "";
    [Range(1, 24 * 60)] public int AccessMinutes { get; set; } = Limits.AccessTokenMinutes;
}

using Ims.Domain;
namespace Ims.Application;

public interface ITokenService
{
    /// <summary>Writes the access token for <paramref name="username"/> from the snapshot: the role, one claim per permission and the data scope (ADR roles-permissions 3.2).</summary>
    string CreateAccessToken(string username, AccessSnapshot access, out DateTime expiresAt);
    string CreateRefreshTokenValue();
    string Hash(string value);
}

using Ims.Domain;
namespace Ims.Application;

public interface ITokenService
{
    string CreateAccessToken(User user, out DateTime expiresAt);
    string CreateRefreshTokenValue();
    string Hash(string value);
}

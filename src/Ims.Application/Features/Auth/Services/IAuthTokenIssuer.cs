using Ims.Domain;
namespace Ims.Application.Features.Auth;

/// <summary>Creates the access/refresh token pair for a user. Shared by login and refresh.</summary>
public interface IAuthTokenIssuer
{
    Task<AuthResult> IssueAsync(User user, CancellationToken ct);
}

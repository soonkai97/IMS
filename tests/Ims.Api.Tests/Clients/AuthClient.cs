using Ims.Api.Tests.Harness;
using Ims.Application.Features.Auth;

namespace Ims.Api.Tests.Clients;

public sealed class AuthClient(ApiClient api)
{
    public Task<ApiResponse> LoginAsync(string username, string password) =>
        api.PostAsync("/api/auth/login", new LoginRequest(username, password));

    public Task<ApiResponse> RefreshAsync(string refreshToken) =>
        api.PostAsync("/api/auth/refresh", new RefreshRequest(refreshToken));

    public Task<ApiResponse> LogoutAsync(string refreshToken) =>
        api.PostAsync("/api/auth/logout", new RefreshRequest(refreshToken));

    public Task<ApiResponse> MeAsync() => api.GetAsync("/api/auth/me");
}

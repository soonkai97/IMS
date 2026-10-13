using Ims.Api.Tests.Harness;
using Ims.Application.Features.Users;

namespace Ims.Api.Tests.Clients;

public sealed class UsersClient(ApiClient api)
{
    public Task<ApiResponse> ListAsync() => api.GetAsync("/api/users");
    public Task<ApiResponse> CreateAsync(UserSaveRequest request) => api.PostAsync("/api/users", request);
    public Task<ApiResponse> UpdateAsync(Guid id, UserSaveRequest request) => api.PutAsync($"/api/users/{id}", request);
}

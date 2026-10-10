using Ims.Api.Tests.Harness;
using Ims.Application.Features.Catalog;

namespace Ims.Api.Tests.Clients;

public sealed class CategoriesClient(ApiClient api)
{
    public Task<ApiResponse> ListAsync() => api.GetAsync("/api/categories");
    public Task<ApiResponse> CreateAsync(string name) => api.PostAsync("/api/categories", new CategorySaveRequest(name));
    public Task<ApiResponse> UpdateAsync(int id, string name) => api.PutAsync($"/api/categories/{id}", new CategorySaveRequest(name));
}

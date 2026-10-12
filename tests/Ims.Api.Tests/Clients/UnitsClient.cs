using Ims.Api.Tests.Harness;
using Ims.Application.Features.Catalog;

namespace Ims.Api.Tests.Clients;

public sealed class UnitsClient(ApiClient api)
{
    public Task<ApiResponse> ListAsync() => api.GetAsync("/api/units");
    public Task<ApiResponse> CreateAsync(string code, string name) => api.PostAsync("/api/units", new UomSaveRequest(code, name));
    public Task<ApiResponse> UpdateAsync(Guid id, string code, string name) => api.PutAsync($"/api/units/{id}", new UomSaveRequest(code, name));
}

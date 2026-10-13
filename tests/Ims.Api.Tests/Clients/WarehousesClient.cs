using Ims.Api.Tests.Harness;
using Ims.Application.Features.Warehouses;

namespace Ims.Api.Tests.Clients;

public sealed class WarehousesClient(ApiClient api)
{
    public Task<ApiResponse> ListAsync(bool includeInactive = false) =>
        api.GetAsync($"/api/warehouses?includeInactive={includeInactive.ToString().ToLowerInvariant()}");

    public Task<ApiResponse> CreateAsync(WarehouseSaveRequest request) => api.PostAsync("/api/warehouses", request);
    public Task<ApiResponse> UpdateAsync(Guid id, WarehouseSaveRequest request) => api.PutAsync($"/api/warehouses/{id}", request);
}

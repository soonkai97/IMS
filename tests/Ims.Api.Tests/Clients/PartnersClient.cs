using Ims.Api.Tests.Harness;
using Ims.Application.Features.Partners;
using Ims.Domain;

namespace Ims.Api.Tests.Clients;

public sealed class PartnersClient(ApiClient api)
{
    public Task<ApiResponse> ListAsync(PartnerType? type = null, bool includeInactive = false, string? search = null, int page = 1, int pageSize = 100)
    {
        var query = $"?includeInactive={includeInactive.ToString().ToLowerInvariant()}&page={page}&pageSize={pageSize}";
        if (type is not null) query += $"&type={type}";
        if (search is not null) query += $"&search={Uri.EscapeDataString(search)}";
        return api.GetAsync("/api/partners" + query);
    }

    public Task<ApiResponse> CreateAsync(PartnerSaveRequest request) => api.PostAsync("/api/partners", request);
    public Task<ApiResponse> UpdateAsync(int id, PartnerSaveRequest request) => api.PutAsync($"/api/partners/{id}", request);
}

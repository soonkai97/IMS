using Ims.Api.Tests.Harness;
using Ims.Application.Features.Catalog;

namespace Ims.Api.Tests.Clients;

public sealed class ProductsClient(ApiClient api)
{
    public Task<ApiResponse> ListAsync(string? search = null, bool? active = null, int? categoryId = null, int page = 1, int pageSize = 50)
    {
        var query = $"?page={page}&pageSize={pageSize}";
        if (search is not null) query += $"&search={Uri.EscapeDataString(search)}";
        if (active is not null) query += $"&active={active.ToString()!.ToLowerInvariant()}";
        if (categoryId is not null) query += $"&categoryId={categoryId}";
        return api.GetAsync("/api/products" + query);
    }

    public Task<ApiResponse> GetAsync(int id) => api.GetAsync($"/api/products/{id}");
    public Task<ApiResponse> LookupAsync(string code) => api.GetAsync($"/api/products/lookup?code={Uri.EscapeDataString(code)}");
    public Task<ApiResponse> CreateAsync(ProductSaveRequest request) => api.PostAsync("/api/products", request);
    public Task<ApiResponse> UpdateAsync(int id, ProductSaveRequest request) => api.PutAsync($"/api/products/{id}", request);
}

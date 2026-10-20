using Ims.Api.Tests.Harness;
using Ims.Application.Features.Purchasing;
using Ims.Domain;

namespace Ims.Api.Tests.Clients;

public sealed class PurchaseOrdersClient(ApiClient api)
{
    public Task<ApiResponse> ListAsync(PurchaseOrderStatus? status = null, Guid? supplierId = null, Guid? warehouseId = null, string? search = null,
        string? from = null, string? to = null, int pageSize = 50, int page = 1, string? cursor = null, bool includeTotal = true)
    {
        var query = $"?pageSize={pageSize}&page={page}&includeTotal={includeTotal.ToString().ToLowerInvariant()}";
        if (status is not null) query += $"&status={status}";
        if (supplierId is not null) query += $"&supplierId={supplierId}";
        if (warehouseId is not null) query += $"&warehouseId={warehouseId}";
        if (search is not null) query += $"&search={Uri.EscapeDataString(search)}";
        if (from is not null) query += $"&from={from}";
        if (to is not null) query += $"&to={to}";
        if (cursor is not null) query += $"&cursor={Uri.EscapeDataString(cursor)}";
        return api.GetAsync("/api/purchase-orders" + query);
    }

    public Task<ApiResponse> GetAsync(Guid id) => api.GetAsync($"/api/purchase-orders/{id}");
    public Task<ApiResponse> CreateAsync(PurchaseOrderSaveRequest request) => api.PostAsync("/api/purchase-orders", request);

    /// <summary>A save with an <c>If-Match</c> header: the given token (quoted like an ETag unless it already is), or "*" (any version) when a test does not care.</summary>
    public Task<ApiResponse> UpdateAsync(Guid id, PurchaseOrderSaveRequest request, string? ifMatch = null) =>
        api.SendAsync(HttpMethod.Put, $"/api/purchase-orders/{id}", request, r => r.Headers.TryAddWithoutValidation("If-Match", Quote(ifMatch ?? "*")));

    /// <summary>A save with NO If-Match header at all, to test that the server demands one (428).</summary>
    public Task<ApiResponse> UpdateWithoutIfMatchAsync(Guid id, PurchaseOrderSaveRequest request) => api.SendAsync(HttpMethod.Put, $"/api/purchase-orders/{id}", request);

    private static string Quote(string token) => token == "*" || token.StartsWith('"') ? token : $"\"{token}\"";
}

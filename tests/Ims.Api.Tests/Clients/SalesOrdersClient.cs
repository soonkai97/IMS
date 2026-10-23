using Ims.Api.Tests.Harness;
using Ims.Application.Features.Sales;
using Ims.Domain;

namespace Ims.Api.Tests.Clients;

public sealed class SalesOrdersClient(ApiClient api)
{
    public Task<ApiResponse> ListAsync(SalesOrderStatus? status = null, Guid? customerId = null, Guid? warehouseId = null, string? search = null,
        string? from = null, string? to = null, int pageSize = 50, int page = 1, string? cursor = null, bool includeTotal = true)
    {
        var query = $"?pageSize={pageSize}&page={page}&includeTotal={includeTotal.ToString().ToLowerInvariant()}";
        if (status is not null) query += $"&status={status}";
        if (customerId is not null) query += $"&customerId={customerId}";
        if (warehouseId is not null) query += $"&warehouseId={warehouseId}";
        if (search is not null) query += $"&search={Uri.EscapeDataString(search)}";
        if (from is not null) query += $"&from={from}";
        if (to is not null) query += $"&to={to}";
        if (cursor is not null) query += $"&cursor={Uri.EscapeDataString(cursor)}";
        return api.GetAsync("/api/sales-orders" + query);
    }

    public Task<ApiResponse> GetAsync(Guid id) => api.GetAsync($"/api/sales-orders/{id}");
    public Task<ApiResponse> CreateAsync(SalesOrderSaveRequest request) => api.PostAsync("/api/sales-orders", request);

    /// <summary>A save with an <c>If-Match</c> header: the given token (quoted like an ETag unless it already is), or the order's current token when a test does not care.</summary>
    public async Task<ApiResponse> UpdateAsync(Guid id, SalesOrderSaveRequest request, string? ifMatch = null)
    {
        ifMatch ??= await CurrentTokenAsync(id);
        return await api.SendAsync(HttpMethod.Put, $"/api/sales-orders/{id}", request, r => r.Headers.TryAddWithoutValidation("If-Match", Quote(ifMatch)));
    }

    /// <summary>A save with NO If-Match header at all, to test that the server demands one (428).</summary>
    public Task<ApiResponse> UpdateWithoutIfMatchAsync(Guid id, SalesOrderSaveRequest request) => api.SendAsync(HttpMethod.Put, $"/api/sales-orders/{id}", request);

    /// <summary>The token the order has now; a placeholder when it cannot be read, so a 403 or 404 still comes from the endpoint and not from a missing header.</summary>
    private async Task<string> CurrentTokenAsync(Guid id)
    {
        var read = await api.GetAsync($"/api/sales-orders/{id}");
        return read.IsSuccess ? read.Data<SalesOrderDto>().ConcurrencyToken : "unreadable";
    }

    private static string Quote(string token) => token == "*" || token.StartsWith('"') ? token : $"\"{token}\"";
}

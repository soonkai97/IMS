using Ims.Api.Tests.Harness;

namespace Ims.Api.Tests.Clients;

public sealed class StockClient(ApiClient api)
{
    public Task<ApiResponse> QueryAsync(int? productId = null, Guid? warehouseId = null, string? search = null, bool lowOnly = false, int pageSize = 100)
    {
        var query = $"?pageSize={pageSize}&lowOnly={lowOnly.ToString().ToLowerInvariant()}";
        if (productId is not null) query += $"&productId={productId}";
        if (warehouseId is not null) query += $"&warehouseId={warehouseId}";
        if (search is not null) query += $"&search={Uri.EscapeDataString(search)}";
        return api.GetAsync("/api/stock" + query);
    }

    public Task<ApiResponse> LedgerAsync(int? productId = null, Guid? warehouseId = null, int? sourceDocId = null, int page = 1, int pageSize = 500)
    {
        var query = $"?page={page}&pageSize={pageSize}";
        if (productId is not null) query += $"&productId={productId}";
        if (warehouseId is not null) query += $"&warehouseId={warehouseId}";
        if (sourceDocId is not null) query += $"&sourceDocId={sourceDocId}";
        return api.GetAsync("/api/stock/ledger" + query);
    }

    public Task<ApiResponse> LowStockAsync(Guid? warehouseId = null, int pageSize = 500) =>
        api.GetAsync($"/api/stock/low-stock?pageSize={pageSize}" + (warehouseId is null ? "" : $"&warehouseId={warehouseId}"));
    public Task<ApiResponse> SummaryAsync() => api.GetAsync("/api/stock/summary");
}

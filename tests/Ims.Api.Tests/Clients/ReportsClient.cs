using Ims.Api.Tests.Harness;

namespace Ims.Api.Tests.Clients;

public sealed class ReportsClient(ApiClient api)
{
    public Task<ApiResponse> MovementsAsync(int? productId = null) =>
        api.GetAsync("/api/reports/movements" + ProductQuery(productId));

    public Task<ApiResponse> StockSummaryExportAsync(int? productId = null) =>
        api.GetAsync("/api/reports/stock-summary/export" + ProductQuery(productId));

    public Task<ApiResponse> MovementsExportAsync(int? productId = null) =>
        api.GetAsync("/api/reports/movements/export" + ProductQuery(productId));

    public Task<ApiResponse> LedgerExportAsync(int? productId = null) =>
        api.GetAsync("/api/reports/ledger/export" + ProductQuery(productId));

    public Task<ApiResponse> LowStockExportAsync() => api.GetAsync("/api/reports/low-stock/export");

    private static string ProductQuery(int? productId) => productId is null ? "" : $"?productId={productId}";
}

using Ims.Api.Tests.Clients;
using Ims.Application;
using Ims.Application.Features.Stock;

namespace Ims.Api.Tests.Support;

/// <summary>Reads the low-stock list to its end, so a test that counts or searches it sees every row and not the first page.</summary>
public static class LowStockPages
{
    /// <summary>Every row of the low-stock list (all pages, 500 at a time), for all warehouses the caller can see or for one.</summary>
    public static async Task<List<LowStockAlert>> ReadAsync(ApiClient who, Guid? warehouseId = null)
    {
        var rows = new List<LowStockAlert>();
        for (var page = 1; ; page++)
        {
            var result = (await who.GetAsync($"/api/stock/low-stock?page={page}&pageSize=500" + (warehouseId is null ? "" : $"&warehouseId={warehouseId}"))).Data<PagedResult<LowStockAlert>>();
            rows.AddRange(result.Items);
            if (rows.Count >= result.Total || result.Items.Count == 0) return rows;
        }
    }
}

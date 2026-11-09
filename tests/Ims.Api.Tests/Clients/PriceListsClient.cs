using Ims.Api.Tests.Harness;
using Ims.Application.Features.Sales;

namespace Ims.Api.Tests.Clients;

public sealed class PriceListsClient(ApiClient api)
{
    public Task<ApiResponse> ListAsync(string? search = null, int pageSize = 50, int page = 1)
    {
        var query = $"?pageSize={pageSize}&page={page}";
        if (search is not null) query += $"&search={Uri.EscapeDataString(search)}";
        return api.GetAsync("/api/price-lists" + query);
    }

    public Task<ApiResponse> GetAsync(Guid id) => api.GetAsync($"/api/price-lists/{id}");

    public Task<ApiResponse> LookupAsync(Guid customerId, Guid productId, Guid? uomId = null) =>
        api.GetAsync($"/api/price-lists/lookup?customerId={customerId}&productId={productId}" + (uomId is null ? "" : $"&uomId={uomId}"));
    public Task<ApiResponse> CreateAsync(PriceListSaveRequest request) => api.PostAsync("/api/price-lists", request);

    /// <summary>A save with an <c>If-Match</c> header: the given token (quoted like an ETag unless it already is), or the list's current token when a test does not care.</summary>
    public async Task<ApiResponse> UpdateAsync(Guid id, PriceListSaveRequest request, string? ifMatch = null)
    {
        ifMatch ??= await CurrentTokenAsync(id);
        return await api.SendAsync(HttpMethod.Put, $"/api/price-lists/{id}", request, r => r.Headers.TryAddWithoutValidation("If-Match", Quote(ifMatch)));
    }

    public async Task<ApiResponse> SetCustomerAsync(Guid id, Guid? customerId, string? ifMatch = null)
    {
        ifMatch ??= await CurrentTokenAsync(id);
        return await api.SendAsync(HttpMethod.Put, $"/api/price-lists/{id}/customer", new PriceListCustomerRequest(customerId), r => r.Headers.TryAddWithoutValidation("If-Match", Quote(ifMatch)));
    }

    public async Task<ApiResponse> DeleteAsync(Guid id, string? ifMatch = null)
    {
        ifMatch ??= await CurrentTokenAsync(id);
        return await api.SendAsync(HttpMethod.Delete, $"/api/price-lists/{id}", null, r => r.Headers.TryAddWithoutValidation("If-Match", Quote(ifMatch)));
    }

    /// <summary>A save with NO If-Match header at all, to test that the server demands one (428).</summary>
    public Task<ApiResponse> UpdateWithoutIfMatchAsync(Guid id, PriceListSaveRequest request) => api.SendAsync(HttpMethod.Put, $"/api/price-lists/{id}", request);

    public Task<ApiResponse> DeleteWithoutIfMatchAsync(Guid id) => api.SendAsync(HttpMethod.Delete, $"/api/price-lists/{id}");

    /// <summary>The token the list has now; a placeholder when it cannot be read, so a 403 or 404 still comes from the endpoint and not from a missing header.</summary>
    private async Task<string> CurrentTokenAsync(Guid id)
    {
        var read = await api.GetAsync($"/api/price-lists/{id}");
        return read.IsSuccess ? read.Data<PriceListDto>().ConcurrencyToken : "unreadable";
    }

    private static string Quote(string token) => token == "*" || token.StartsWith('"') ? token : $"\"{token}\"";
}

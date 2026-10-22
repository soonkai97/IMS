using Ims.Api.Tests.Harness;
using Ims.Application.Features.Documents;
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

    /// <summary>The reorder suggestions of a warehouse; a null id sends no <c>warehouseId</c> at all (the 400 of a missing one).</summary>
    public Task<ApiResponse> SuggestionsAsync(Guid? warehouseId, string? search = null, int page = 1, int pageSize = 50)
    {
        var query = $"?page={page}&pageSize={pageSize}";
        if (warehouseId is not null) query += $"&warehouseId={warehouseId}";
        if (search is not null) query += $"&search={Uri.EscapeDataString(search)}";
        return api.GetAsync("/api/purchase-orders/suggestions" + query);
    }

    public Task<ApiResponse> GetAsync(Guid id) => api.GetAsync($"/api/purchase-orders/{id}");
    public Task<ApiResponse> CreateAsync(PurchaseOrderSaveRequest request) => api.PostAsync("/api/purchase-orders", request);

    /// <summary>A save with an <c>If-Match</c> header: the given token (quoted like an ETag unless it already is), or the order's current token when a test does not care.</summary>
    public Task<ApiResponse> UpdateAsync(Guid id, PurchaseOrderSaveRequest request, string? ifMatch = null) => Send(HttpMethod.Put, $"/api/purchase-orders/{id}", request, ifMatch);

    /// <summary>Creates the Draft receipt of an Approved order (no body, no If-Match: it makes a new document).</summary>
    public Task<ApiResponse> CreateReceiptAsync(Guid id) => api.PostAsync($"/api/purchase-orders/{id}/receipt", null);

    /// <summary>Sends a write with the given <c>If-Match</c> token, or the order's current one (the API refuses "*" on a purchase order write).</summary>
    private async Task<ApiResponse> Send(HttpMethod method, string url, object? body, string? ifMatch)
    {
        ifMatch ??= await CurrentTokenAsync(url);
        return await api.SendAsync(method, url, body, r => r.Headers.TryAddWithoutValidation("If-Match", Quote(ifMatch)));
    }

    /// <summary>The token the order has now; a placeholder when it cannot be read, so a 403 or 404 still comes from the endpoint and not from a missing header.</summary>
    private async Task<string> CurrentTokenAsync(string url)
    {
        var read = await api.GetAsync(string.Join('/', url.Split('/').Take(4)));
        return read.IsSuccess ? read.Data<PurchaseOrderDto>().ConcurrencyToken : "unreadable";
    }

    public Task<ApiResponse> SubmitAsync(Guid id, string? ifMatch = null) => Send(HttpMethod.Post, $"/api/purchase-orders/{id}/submit", null, ifMatch);
    public Task<ApiResponse> ApproveAsync(Guid id, string? ifMatch = null) => Send(HttpMethod.Post, $"/api/purchase-orders/{id}/approve", null, ifMatch);
    public Task<ApiResponse> RejectAsync(Guid id, string reason, string? ifMatch = null) => Send(HttpMethod.Post, $"/api/purchase-orders/{id}/reject", new RejectRequest(reason), ifMatch);
    public Task<ApiResponse> RecallAsync(Guid id, string? ifMatch = null) => Send(HttpMethod.Post, $"/api/purchase-orders/{id}/recall", null, ifMatch);
    public Task<ApiResponse> CancelAsync(Guid id, string? reason = null, string? ifMatch = null) => Send(HttpMethod.Post, $"/api/purchase-orders/{id}/cancel", new CancelRequest(reason), ifMatch);
    public Task<ApiResponse> CloseAsync(Guid id, string? reason, string? ifMatch = null) => Send(HttpMethod.Post, $"/api/purchase-orders/{id}/close", new CloseRequest(reason), ifMatch);

    /// <summary>A transition with NO If-Match header at all, to test that the server demands one (428).</summary>
    public Task<ApiResponse> TransitionWithoutIfMatchAsync(Guid id, string action, object? body = null) => api.SendAsync(HttpMethod.Post, $"/api/purchase-orders/{id}/{action}", body);

    /// <summary>A save with NO If-Match header at all, to test that the server demands one (428).</summary>
    public Task<ApiResponse> UpdateWithoutIfMatchAsync(Guid id, PurchaseOrderSaveRequest request) => api.SendAsync(HttpMethod.Put, $"/api/purchase-orders/{id}", request);

    private static string Quote(string token) => token == "*" || token.StartsWith('"') ? token : $"\"{token}\"";
}

using Ims.Api.Tests.Harness;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Clients;

public sealed class DocumentsClient(ApiClient api)
{
    public Task<ApiResponse> ListAsync(DocumentType? type = null, DocumentStatus? status = null, int? warehouseId = null, string? search = null, int pageSize = 50,
        bool? mine = null, bool? requiresApproval = null)
    {
        var query = $"?pageSize={pageSize}";
        if (type is not null) query += $"&type={type}";
        if (status is not null) query += $"&status={status}";
        if (warehouseId is not null) query += $"&warehouseId={warehouseId}";
        if (search is not null) query += $"&search={Uri.EscapeDataString(search)}";
        if (mine is not null) query += $"&mine={mine.ToString()!.ToLowerInvariant()}";
        if (requiresApproval is not null) query += $"&requiresApproval={requiresApproval.ToString()!.ToLowerInvariant()}";
        return api.GetAsync("/api/documents" + query);
    }

    public Task<ApiResponse> CountsAsync() => api.GetAsync("/api/documents/counts");
    public Task<ApiResponse> GetAsync(int id) => api.GetAsync($"/api/documents/{id}");
    public Task<ApiResponse> CreateReceiptAsync(DocumentSaveRequest request) => api.PostAsync("/api/documents/receipts", request);
    public Task<ApiResponse> CreateIssueAsync(DocumentSaveRequest request) => api.PostAsync("/api/documents/issues", request);
    public Task<ApiResponse> UpdateAsync(int id, DocumentSaveRequest request, string? ifMatch = null) => Send(HttpMethod.Put, $"/api/documents/{id}", request, ifMatch);
    public Task<ApiResponse> SubmitAsync(int id, string? ifMatch = null) => Send(HttpMethod.Post, $"/api/documents/{id}/submit", null, ifMatch);
    public Task<ApiResponse> ApproveAsync(int id, string? ifMatch = null) => Send(HttpMethod.Post, $"/api/documents/{id}/approve", null, ifMatch);
    public Task<ApiResponse> RejectAsync(int id, string reason, string? ifMatch = null) => Send(HttpMethod.Post, $"/api/documents/{id}/reject", new RejectRequest(reason), ifMatch);
    public Task<ApiResponse> CancelAsync(int id, string? reason = null, string? ifMatch = null) => Send(HttpMethod.Post, $"/api/documents/{id}/cancel", new CancelRequest(reason), ifMatch);
    /// <summary>Approves with the override reason in the body (needed when the document <c>requiresOverride</c>).</summary>
    public Task<ApiResponse> ApproveWithReasonAsync(int id, string? negativeStockOverrideReason, string? ifMatch = null) =>
        Send(HttpMethod.Post, $"/api/documents/{id}/approve", new ApproveRequest(negativeStockOverrideReason), ifMatch);
    /// <summary>Posting has no request body: the override reason was recorded when the document was approved.</summary>
    public Task<ApiResponse> PostAsync(int id, string? ifMatch = null) => Send(HttpMethod.Post, $"/api/documents/{id}/post", null, ifMatch);
    public Task<ApiResponse> ReverseAsync(int id, string reason, string? ifMatch = null) => Send(HttpMethod.Post, $"/api/documents/{id}/reverse", new ReverseRequest(reason), ifMatch);

    /// <summary>Sends a write with an optional <c>If-Match</c> header. The token is quoted like an ETag unless it already is (or is "*").</summary>
    private Task<ApiResponse> Send(HttpMethod method, string url, object? body, string? ifMatch) =>
        api.SendAsync(method, url, body, ifMatch is null ? null : r => r.Headers.TryAddWithoutValidation("If-Match", Quote(ifMatch)));

    private static string Quote(string token) => token == "*" || token.StartsWith('"') ? token : $"\"{token}\"";
}

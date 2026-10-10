using Ims.Api.Tests.Harness;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Clients;

public sealed class DocumentsClient(ApiClient api)
{
    public Task<ApiResponse> ListAsync(DocumentType? type = null, DocumentStatus? status = null, int? warehouseId = null, string? search = null, int pageSize = 50)
    {
        var query = $"?pageSize={pageSize}";
        if (type is not null) query += $"&type={type}";
        if (status is not null) query += $"&status={status}";
        if (warehouseId is not null) query += $"&warehouseId={warehouseId}";
        if (search is not null) query += $"&search={Uri.EscapeDataString(search)}";
        return api.GetAsync("/api/documents" + query);
    }

    public Task<ApiResponse> GetAsync(int id) => api.GetAsync($"/api/documents/{id}");
    public Task<ApiResponse> CreateReceiptAsync(DocumentSaveRequest request) => api.PostAsync("/api/documents/receipts", request);
    public Task<ApiResponse> CreateIssueAsync(DocumentSaveRequest request) => api.PostAsync("/api/documents/issues", request);
    public Task<ApiResponse> UpdateAsync(int id, DocumentSaveRequest request) => api.PutAsync($"/api/documents/{id}", request);
    public Task<ApiResponse> SubmitAsync(int id) => api.PostAsync($"/api/documents/{id}/submit");
    public Task<ApiResponse> ApproveAsync(int id) => api.PostAsync($"/api/documents/{id}/approve");
    public Task<ApiResponse> RejectAsync(int id, string reason) => api.PostAsync($"/api/documents/{id}/reject", new RejectRequest(reason));
    public Task<ApiResponse> CancelAsync(int id, string? reason = null) => api.PostAsync($"/api/documents/{id}/cancel", new CancelRequest(reason));
    public Task<ApiResponse> PostAsync(int id, string? negativeStockOverrideReason = null) =>
        api.PostAsync($"/api/documents/{id}/post", new PostRequest(negativeStockOverrideReason));
    public Task<ApiResponse> ReverseAsync(int id, string reason) => api.PostAsync($"/api/documents/{id}/reverse", new ReverseRequest(reason));
}

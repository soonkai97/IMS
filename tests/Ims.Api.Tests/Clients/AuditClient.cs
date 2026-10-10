using Ims.Api.Tests.Harness;

namespace Ims.Api.Tests.Clients;

public sealed class AuditClient(ApiClient api)
{
    public Task<ApiResponse> QueryAsync(string? actor = null, string? entityType = null, int pageSize = 200, string? entityId = null, string? action = null,
        string? search = null)
    {
        var query = $"?pageSize={pageSize}";
        if (actor is not null) query += $"&actor={Uri.EscapeDataString(actor)}";
        if (entityType is not null) query += $"&entityType={Uri.EscapeDataString(entityType)}";
        if (entityId is not null) query += $"&entityId={Uri.EscapeDataString(entityId)}";
        if (action is not null) query += $"&action={Uri.EscapeDataString(action)}";
        if (search is not null) query += $"&search={Uri.EscapeDataString(search)}";
        return api.GetAsync("/api/audit" + query);
    }

    public Task<ApiResponse> FiltersAsync() => api.GetAsync("/api/audit/filters");
}

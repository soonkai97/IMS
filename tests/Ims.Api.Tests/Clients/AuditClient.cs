using Ims.Api.Tests.Harness;

namespace Ims.Api.Tests.Clients;

public sealed class AuditClient(ApiClient api)
{
    public Task<ApiResponse> QueryAsync(string? actor = null, string? entityType = null, int pageSize = 200)
    {
        var query = $"?pageSize={pageSize}";
        if (actor is not null) query += $"&actor={Uri.EscapeDataString(actor)}";
        if (entityType is not null) query += $"&entityType={Uri.EscapeDataString(entityType)}";
        return api.GetAsync("/api/audit" + query);
    }
}

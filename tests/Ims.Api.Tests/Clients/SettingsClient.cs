using Ims.Api.Tests.Harness;
using Ims.Application.Features.Settings;

namespace Ims.Api.Tests.Clients;

/// <summary>System settings and approval rules.</summary>
public sealed class SettingsClient(ApiClient api)
{
    public Task<ApiResponse> GetAsync() => api.GetAsync("/api/settings");
    public Task<ApiResponse> UpdateAsync(SettingsDto settings) => api.PutAsync("/api/settings", settings);
    public Task<ApiResponse> ListRulesAsync() => api.GetAsync("/api/approval-rules");
    public Task<ApiResponse> CreateRuleAsync(ApprovalRuleSaveRequest rule) => api.PostAsync("/api/approval-rules", rule);
    public Task<ApiResponse> UpdateRuleAsync(Guid id, ApprovalRuleSaveRequest rule) => api.PutAsync($"/api/approval-rules/{id}", rule);
}

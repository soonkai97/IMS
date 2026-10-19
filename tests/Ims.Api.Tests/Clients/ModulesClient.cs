using Ims.Api.Tests.Harness;
using Ims.Application.Features.Modules;

namespace Ims.Api.Tests.Clients;

/// <summary>The module switches.</summary>
public sealed class ModulesClient(ApiClient api)
{
    public Task<ApiResponse> ListAsync() => api.GetAsync("/api/modules");
    public Task<ApiResponse> SwitchAsync(string code, bool? enabled, string reason) => api.PutAsync($"/api/modules/{code}", new ModuleSwitchRequest(enabled, reason));
}

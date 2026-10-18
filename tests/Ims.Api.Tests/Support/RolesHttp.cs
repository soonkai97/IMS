using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Roles;

namespace Ims.Api.Tests.Support;

/// <summary>The role endpoints as a caller uses them, with the If-Match the permission replace needs.</summary>
public static class RolesHttp
{
    public static Task<ApiResponse> CreateAsync(this ApiClient c, string name, params string[] permissions) =>
        c.PostAsync("/api/roles", new RoleCreateRequest(name, null, null, permissions));

    public static Task<ApiResponse> UpdateRoleAsync(this ApiClient c, RoleDto role, string? name = null, bool? active = null, string? ifMatch = null) =>
        c.SendAsync(HttpMethod.Put, $"/api/roles/{role.Id}", new RoleUpdateRequest(name ?? role.Name, role.Description, active ?? role.IsActive), IfMatch(ifMatch));

    public static Task<ApiResponse> ReplaceAsync(this ApiClient c, RoleDto role, string[] permissions, string? ifMatch = null) =>
        c.SendAsync(HttpMethod.Put, $"/api/roles/{role.Id}/permissions", new RolePermissionsRequest(permissions), IfMatch(ifMatch ?? role.ConcurrencyToken));

    public static Task<ApiResponse> DeleteRoleAsync(this ApiClient c, RoleDto role) => c.DeleteAsync($"/api/roles/{role.Id}");

    private static Action<HttpRequestMessage> IfMatch(string? token) => r => { if (token is not null) r.Headers.TryAddWithoutValidation("If-Match", $"\"{token}\""); };
}

using Ims.Application;
using Ims.Application.Features.Roles;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Ims.Api.Features.Roles;

/// <summary>
/// Roles and what they may do. A change applies to a signed-in person at their next token refresh, which is at most the token lifetime plus the clock skew (about 5.5 minutes): the
/// rights ride in the access token and nothing is looked up per request. Every response that carries a role also carries its <c>ETag</c>; a permission replace must send it back
/// as <c>If-Match</c> (428 without it, 409 when it is stale).
/// </summary>
[Route(ApiRoutes.Roles)]
public class RolesController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [HttpGet, Authorize(Policy = Permissions.RolesRead)]
    [ProducesResponseType<IReadOnlyList<RoleDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] RoleFilter filter) => SendOk(new ListRolesQuery(filter));

    [HttpGet(ApiRoutes.PublicId), Authorize(Policy = Permissions.RolesRead)]
    [ProducesResponseType<RoleDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id) => WithETag(await Send(new GetRoleQuery(id)));

    [HttpPost, Authorize(Policy = Permissions.RolesManage)]
    [ProducesResponseType<RoleDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] RoleCreateRequest body) => WithETag(await Send(new CreateRoleCommand(body)));

    [HttpPut(ApiRoutes.PublicId), Authorize(Policy = Permissions.RolesManage)]
    [ProducesResponseType<RoleDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] RoleUpdateRequest body) =>
        WithETag(await Send(new UpdateRoleCommand(id, body, IfMatchHeader.Read(Request, required: false))));

    [HttpPut(ApiRoutes.PublicId + "/permissions"), Authorize(Policy = Permissions.RolesManage)]
    [ProducesResponseType<RolePermissionsResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReplacePermissions(Guid id, [FromBody] RolePermissionsRequest body)
    {
        var result = await Send(new ReplaceRolePermissionsCommand(id, body, IfMatchHeader.Read(Request, required: true)));
        Response.Headers[HeaderNames.ETag] = ETagOf(result.Role);
        return Ok(result);
    }

    [HttpDelete(ApiRoutes.PublicId), Authorize(Policy = Permissions.RolesManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await Send(new DeleteRoleCommand(id));
        return NoContent();
    }

    private static string ETagOf(RoleDto role) => $"\"{role.ConcurrencyToken}\"";

    private IActionResult WithETag(RoleDto role)
    {
        Response.Headers[HeaderNames.ETag] = ETagOf(role);
        return Ok(role);
    }
}

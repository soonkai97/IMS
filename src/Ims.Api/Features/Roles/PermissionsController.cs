using Ims.Application;
using Ims.Application.Features.Roles;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Ims.Api.Features.Roles;

/// <summary>The permission catalog: what a role can hold.</summary>
[Route(ApiRoutes.Permissions)]
public class PermissionsController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [HttpGet, Authorize(Policy = Permissions.RolesRead)]
    [ProducesResponseType<IReadOnlyList<PermissionDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List() => SendOk(new ListPermissionsQuery());
}

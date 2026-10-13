using Ims.Application;
using Ims.Application.Features.Users;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ims.Api.Features.Users;

[Route(ApiRoutes.Users)]
public class UsersController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [HttpGet, Authorize(Policy = Permissions.UsersRead)]
    [ProducesResponseType<PagedResult<UserDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] UserFilter filter) => SendOk(new ListUsersQuery(filter));

    [HttpPost, Authorize(Policy = Permissions.UsersManage)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Create([FromBody] UserSaveRequest body) => SendOk(new CreateUserCommand(body));

    [HttpPut(ApiRoutes.PublicId), Authorize(Policy = Permissions.UsersManage)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Update(Guid id, [FromBody] UserSaveRequest body) => SendOk(new UpdateUserCommand(id, body));
}

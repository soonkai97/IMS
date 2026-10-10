using Ims.Application;
using Ims.Application.Features.Settings;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Ims.Api.Features.Settings;

[Route(ApiRoutes.Settings)]
public class SettingsController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [HttpGet, Authorize(Policy = Permissions.MasterDataRead)]
    [ProducesResponseType<SettingsDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Get() => SendOk(new GetSettingsQuery());

    [HttpPut, Authorize(Policy = Permissions.SettingsManage)]
    [ProducesResponseType<SettingsDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Update([FromBody] SettingsDto body) => SendOk(new UpdateSettingsCommand(body));
}

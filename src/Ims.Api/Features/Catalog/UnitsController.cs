using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Ims.Api.Features.Catalog;

[Route(ApiRoutes.Units)]
public class UnitsController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [HttpGet, Authorize(Policy = Permissions.MasterDataRead)]
    [ProducesResponseType<IReadOnlyList<UomDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List() => SendOk(new ListUomsQuery());

    [HttpPost, Authorize(Policy = Permissions.MasterDataManage)]
    [ProducesResponseType<UomDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Create([FromBody] UomSaveRequest body) => SendOk(new CreateUomCommand(body));

    [HttpPut(ApiRoutes.Id), Authorize(Policy = Permissions.MasterDataManage)]
    [ProducesResponseType<UomDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Update(int id, [FromBody] UomSaveRequest body) => SendOk(new UpdateUomCommand(id, body));
}

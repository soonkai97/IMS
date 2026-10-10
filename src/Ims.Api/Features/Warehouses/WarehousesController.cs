using Ims.Application;
using Ims.Application.Features.Warehouses;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Ims.Api.Features.Warehouses;

[Route(ApiRoutes.Warehouses)]
public class WarehousesController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [HttpGet, Authorize(Policy = Permissions.MasterDataRead)]
    [ProducesResponseType<IReadOnlyList<WarehouseDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] bool includeInactive = false) => SendOk(new ListWarehousesQuery(includeInactive));

    [HttpPost, Authorize(Policy = Permissions.MasterDataManage)]
    [ProducesResponseType<WarehouseDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Create([FromBody] WarehouseSaveRequest body) => SendOk(new CreateWarehouseCommand(body));

    [HttpPut(ApiRoutes.Id), Authorize(Policy = Permissions.MasterDataManage)]
    [ProducesResponseType<WarehouseDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Update(int id, [FromBody] WarehouseSaveRequest body) => SendOk(new UpdateWarehouseCommand(id, body));
}

using Ims.Application;
using Ims.Application.Features.Partners;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Ims.Api.Features.Warehouses;

[Route(ApiRoutes.Partners)]
public class PartnersController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [HttpGet, Authorize(Policy = Permissions.MasterDataRead)]
    [ProducesResponseType<PagedResult<PartnerDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] PartnerFilter filter) => SendOk(new ListPartnersQuery(filter));

    [HttpPost, Authorize(Policy = Permissions.PartnersCreate)]
    [ProducesResponseType<PartnerDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Create([FromBody] PartnerSaveRequest body) => SendOk(new CreatePartnerCommand(body));

    [HttpPut(ApiRoutes.PublicId), Authorize(Policy = Permissions.MasterDataManage)]
    [ProducesResponseType<PartnerDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Update(Guid id, [FromBody] PartnerSaveRequest body) => SendOk(new UpdatePartnerCommand(id, body));
}

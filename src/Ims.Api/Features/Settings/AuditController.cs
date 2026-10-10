using Ims.Application;
using Ims.Application.Features.Audit;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Ims.Api.Features.Settings;

[Route(ApiRoutes.Audit)]
public class AuditController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [HttpGet, Authorize(Policy = Permissions.AuditRead)]
    [ProducesResponseType<PagedResult<AuditLogDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] AuditQueryFilter filter) => SendOk(new QueryAuditLogQuery(filter));
}

using Ims.Application;
using Ims.Application.Features.Audit;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Ims.Api.Features.Settings;

[Route(ApiRoutes.Audit)]
public class AuditController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    /// <summary>
    /// The audit log, newest first. Text filters are partial or prefix matches that the indexes answer (no table scan): <c>entityType</c> is a fragment of
    /// the entity name, <c>entityId</c>, <c>actor</c> and <c>action</c> are prefixes, <c>search</c> tries an entity name fragment and an id prefix at once.
    /// </summary>
    [HttpGet, Authorize(Policy = Permissions.AuditRead)]
    [ProducesResponseType<PagedResult<AuditLogDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] AuditQueryFilter filter) => SendOk(new QueryAuditLogQuery(filter));

    /// <summary>The entity names and action names an audit row can carry, for the filter dropdowns.</summary>
    [HttpGet(RouteSegments.Filters), Authorize(Policy = Permissions.AuditRead)]
    [ProducesResponseType<AuditFilterOptions>(StatusCodes.Status200OK)]
    public Task<IActionResult> Filters() => SendOk(new GetAuditFilterOptionsQuery());
}

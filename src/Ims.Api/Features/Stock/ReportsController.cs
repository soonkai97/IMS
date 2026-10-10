using Microsoft.AspNetCore.Http.Timeouts;
using Ims.Application;
using Ims.Application.Features.Reports;
using Ims.Application.Features.Stock;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Ims.Api.Features.Stock;

[Route(ApiRoutes.Reports)]
public class ReportsController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [HttpGet(RouteSegments.Movements), Authorize(Policy = Permissions.ReportsRead)]
    [ProducesResponseType<PagedResult<MovementRow>>(StatusCodes.Status200OK)]
    public Task<IActionResult> Movements([FromQuery] MovementFilter filter) => SendOk(new ListMovementsQuery(filter));

    [RequestTimeout(RequestTimeoutPolicies.Long)]
    [ProducesFile(ContentTypes.Xlsx)]
    [HttpGet(RouteSegments.StockSummaryExport), Authorize(Policy = Permissions.ReportsRead)]
    public async Task<IActionResult> ExportStockSummary([FromQuery] StockFilter filter) => AsFile(await Send(new ExportStockSummaryQuery(filter)));

    [RequestTimeout(RequestTimeoutPolicies.Long)]
    [ProducesFile(ContentTypes.Xlsx)]
    [HttpGet(RouteSegments.MovementsExport), Authorize(Policy = Permissions.ReportsRead)]
    public async Task<IActionResult> ExportMovements([FromQuery] MovementFilter filter) => AsFile(await Send(new ExportMovementsQuery(filter)));

    [RequestTimeout(RequestTimeoutPolicies.Long)]
    [ProducesFile(ContentTypes.Xlsx)]
    [HttpGet(RouteSegments.LedgerExport), Authorize(Policy = Permissions.ReportsRead)]
    public async Task<IActionResult> ExportLedger([FromQuery] LedgerFilter filter) => AsFile(await Send(new ExportLedgerQuery(filter)));

    [RequestTimeout(RequestTimeoutPolicies.Long)]
    [ProducesFile(ContentTypes.Xlsx)]
    [HttpGet(RouteSegments.LowStockExport), Authorize(Policy = Permissions.ReportsRead)]
    public async Task<IActionResult> ExportLowStock() => AsFile(await Send(new ExportLowStockQuery()));

    private FileContentResult AsFile(ExportFile f) => File(f.Content, f.ContentType, f.FileName);
}

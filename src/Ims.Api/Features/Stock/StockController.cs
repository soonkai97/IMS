using Ims.Application;
using Ims.Application.Features.Stock;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Ims.Api.Features.Stock;

[Route(ApiRoutes.Stock)]
public class StockController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [HttpGet, Authorize(Policy = Permissions.StockRead)]
    [ProducesResponseType<PagedResult<StockRow>>(StatusCodes.Status200OK)]
    public Task<IActionResult> Query([FromQuery] StockFilter filter) => SendOk(new QueryStockQuery(filter));

    [HttpGet(RouteSegments.Ledger), Authorize(Policy = Permissions.StockRead)]
    [ProducesResponseType<PagedResult<LedgerRow>>(StatusCodes.Status200OK)]
    public Task<IActionResult> Ledger([FromQuery] LedgerFilter filter) => SendOk(new QueryLedgerQuery(filter));

    [HttpGet(RouteSegments.LowStock), Authorize(Policy = Permissions.StockRead)]
    [ProducesResponseType<PagedResult<LowStockAlert>>(StatusCodes.Status200OK)]
    public Task<IActionResult> LowStock([FromQuery] LowStockFilter filter) => SendOk(new ListLowStockQuery(filter));

    [HttpGet(RouteSegments.Summary), Authorize(Policy = Permissions.StockRead)]
    [ProducesResponseType<StockSummary>(StatusCodes.Status200OK)]
    public Task<IActionResult> Summary() => SendOk(new GetStockSummaryQuery());
}

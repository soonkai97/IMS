using Ims.Application;
using Ims.Application.Features.Sales;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ims.Api.Features.Sales;

/// <summary>
/// The sales module: sales orders (pilot slice PL3.2 builds the Draft: create, edit, read, list; the workflow and the deliveries follow). While the module is off every signed-in caller gets the
/// 404 of an unknown route (<see cref="RequiresModuleAttribute"/>), before the permission is asked. Reading needs <c>so.read</c>, writing <c>so.manage</c>. A selling price, a line amount and an
/// order total are money that only a holder of <c>so.manage</c> sees (absent otherwise); <c>cost.view</c> is not involved, a selling price is not cost. Every response that carries an order also
/// carries its <c>ETag</c>; a write must send it back as <c>If-Match</c> and is refused with 409 when someone else changed the order in the meantime.
/// </summary>
[Route(ApiRoutes.SalesOrders), RequiresModule(ModuleCodes.Sales)]
public class SalesOrdersController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    /// <summary>Sales orders, newest first, in the warehouses the caller may read. Prices and totals are absent for a caller without <c>so.manage</c>.</summary>
    [HttpGet, Authorize(Policy = Permissions.SoRead)]
    [ProducesResponseType<PagedResult<SalesOrderListItem>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] SalesOrderFilter filter) => SendOk(new ListSalesOrdersQuery(filter));

    [HttpGet(ApiRoutes.PublicId), Authorize(Policy = Permissions.SoRead)]
    [ProducesResponseType<SalesOrderDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Get(Guid id) => SendOrder(new GetSalesOrderQuery(id));

    /// <summary>Creates a Draft with the next number of the warehouse and month (<c>SO-&lt;WH&gt;-&lt;yyMM&gt;-&lt;nnnn&gt;</c>).</summary>
    [HttpPost, Authorize(Policy = Permissions.SoManage)]
    [ProducesResponseType<SalesOrderDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Create([FromBody] SalesOrderSaveRequest body) => SendOrder(new CreateSalesOrderCommand(body));

    /// <summary>Replaces the customer, the remark and all lines of a Draft. The warehouse cannot change. <c>If-Match</c> is required (428 without, 409 when stale).</summary>
    [HttpPut(ApiRoutes.PublicId), Authorize(Policy = Permissions.SoManage)]
    [ProducesResponseType<SalesOrderDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Update(Guid id, [FromBody] SalesOrderSaveRequest body) => SendOrder(new UpdateSalesOrderCommand(id, body, IfMatch));

    /// <summary>The concurrency token the client last saw; required on every write (a missing header is 428, see <see cref="IfMatchHeader"/>).</summary>
    private string? IfMatch => IfMatchHeader.Read(Request, required: true);

    private async Task<IActionResult> SendOrder(IRequest<SalesOrderDto> request)
    {
        var order = await Send(request);
        Response.Headers[HeaderNames.ETag] = $"\"{order.ConcurrencyToken}\"";
        return Ok(order);
    }
}

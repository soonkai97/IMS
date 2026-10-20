using Ims.Application;
using Ims.Application.Features.Purchasing;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ims.Api.Features.Purchasing;

/// <summary>
/// The purchasing module: purchase orders (pilot slice PL2.2 builds the draft). While the module is off every signed-in caller gets the 404 of an unknown route
/// (<see cref="RequiresModuleAttribute"/>), before the permission is asked. Reading needs <c>po.read</c>, writing <c>po.manage</c>. Every response that carries an order also
/// carries its <c>ETag</c>; a save must send it back as <c>If-Match</c> and is refused with 409 when someone else changed the order in the meantime.
/// </summary>
[Route(ApiRoutes.PurchaseOrders), RequiresModule(ModuleCodes.Purchasing)]
public class PurchaseOrdersController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    /// <summary>Purchase orders, newest first, in the warehouses the caller may read. Prices and totals are absent for a caller without <c>cost.view</c>.</summary>
    [HttpGet, Authorize(Policy = Permissions.PoRead)]
    [ProducesResponseType<PagedResult<PurchaseOrderListItem>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] PurchaseOrderFilter filter) => SendOk(new ListPurchaseOrdersQuery(filter));

    [HttpGet(ApiRoutes.PublicId), Authorize(Policy = Permissions.PoRead)]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Get(Guid id) => SendOrder(new GetPurchaseOrderQuery(id));

    /// <summary>Creates a Draft with the next number of the warehouse and month (<c>PO-&lt;WH&gt;-&lt;yyMM&gt;-&lt;nnnn&gt;</c>).</summary>
    [HttpPost, Authorize(Policy = Permissions.PoManage)]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Create([FromBody] PurchaseOrderSaveRequest body) => SendOrder(new CreatePurchaseOrderCommand(body));

    /// <summary>Replaces the supplier, the remark and all lines of a Draft. The warehouse cannot change. <c>If-Match</c> is required (428 without, 409 when stale).</summary>
    [HttpPut(ApiRoutes.PublicId), Authorize(Policy = Permissions.PoManage)]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Update(Guid id, [FromBody] PurchaseOrderSaveRequest body) => SendOrder(new UpdatePurchaseOrderCommand(id, body, IfMatchHeader.Read(Request, required: true)));

    private async Task<IActionResult> SendOrder(IRequest<PurchaseOrderDto> request)
    {
        var order = await Send(request);
        Response.Headers[HeaderNames.ETag] = $"\"{order.ConcurrencyToken}\"";
        return Ok(order);
    }
}

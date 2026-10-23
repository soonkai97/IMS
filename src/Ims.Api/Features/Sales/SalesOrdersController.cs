using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Sales;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ims.Api.Features.Sales;

/// <summary>
/// The sales module: sales orders (pilot slices PL3.2 and PL3.3: the Draft and its approval workflow; the deliveries follow). While the module is off every signed-in caller gets the
/// 404 of an unknown route (<see cref="RequiresModuleAttribute"/>), before the permission is asked. Reading needs <c>so.read</c>, writing <c>so.manage</c>; approving and rejecting need the approval tier the order is routed to (<c>document.approve</c> or <c>document.approve.large</c>, there is no sales-order approval permission) and, checked in the command, <c>so.read</c>. A selling price, a line amount and an
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

    /// <summary>
    /// Sends a Draft on. Every line needs a price above 0. When an approval rule fires the order is Submitted and waits for the reviewer of its tier; when none fires it is Approved at once, with no approver.
    /// The total at selling price is what the rules are tested against.
    /// </summary>
    [HttpPost(RouteSegments.Submit), Authorize(Policy = Permissions.SoManage)]
    [ProducesResponseType<SalesOrderDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Submit(Guid id) => SendOrder(new SubmitSalesOrderCommand(id, IfMatch));

    /// <summary>Approves a Submitted order: the reviewer of its tier (a Supervisor for a standard order, the Owner for a large one, an Administrator either), never the person who submitted it unless self-approval is on. The reviewer also needs <c>so.read</c> (checked in the command).</summary>
    [HttpPost(RouteSegments.Approve), Authorize(Policy = AuthPolicies.DocumentReview)]
    [ProducesResponseType<SalesOrderDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Approve(Guid id) => SendOrder(new ApproveSalesOrderCommand(id, IfMatch));

    /// <summary>Returns a Submitted order to Draft with a reason of at least 5 characters, as a review like approving.</summary>
    [HttpPost(RouteSegments.Reject), Authorize(Policy = AuthPolicies.DocumentReview)]
    [ProducesResponseType<SalesOrderDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Reject(Guid id, [FromBody] RejectRequest body) => SendOrder(new RejectSalesOrderCommand(id, body, IfMatch));

    /// <summary>Takes the caller's own Submitted order back to Draft (the Edit button): same number, same lines, no reason. Repeating it on an order that is already an ordinary Draft answers 200 and changes nothing.</summary>
    [HttpPost(RouteSegments.Recall), Authorize(Policy = Permissions.SoManage)]
    [ProducesResponseType<SalesOrderDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Recall(Guid id) => SendOrder(new RecallSalesOrderCommand(id, IfMatch));

    /// <summary>Cancels a Draft (by its creator, or a holder of <c>modules.manage</c>), a Submitted order (by its submitter) or an Approved one (by a reviewer it is routed to). The reason is optional, except that cancelling an Approved order needs one of at least 5 characters.</summary>
    [HttpPost(RouteSegments.Cancel), Authorize(Policy = AuthPolicies.SalesOrderCancel)]
    [ProducesResponseType<SalesOrderDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Cancel(Guid id, [FromBody] CancelRequest body) => SendOrder(new CancelSalesOrderCommand(id, body, IfMatch));

    /// <summary>The concurrency token the client last saw; required on every write (a missing header is 428, see <see cref="IfMatchHeader"/>).</summary>
    private string? IfMatch => IfMatchHeader.Read(Request, required: true);

    private async Task<IActionResult> SendOrder(IRequest<SalesOrderDto> request)
    {
        var order = await Send(request);
        Response.Headers[HeaderNames.ETag] = $"\"{order.ConcurrencyToken}\"";
        return Ok(order);
    }
}

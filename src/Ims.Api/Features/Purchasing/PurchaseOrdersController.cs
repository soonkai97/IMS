using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Purchasing;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ims.Api.Features.Purchasing;

/// <summary>
/// The purchasing module: purchase orders and their workflow (pilot slices PL2.2 and PL2.3). While the module is off every signed-in caller gets the 404 of an unknown route
/// (<see cref="RequiresModuleAttribute"/>), before the permission is asked. Reading needs <c>po.read</c>, writing <c>po.manage</c>; approving and rejecting need the approval tier the order is routed
/// to (<c>document.approve</c> or <c>document.approve.large</c>, there is no purchase-order approval permission) and, checked in the command because an endpoint carries one policy, <c>po.read</c>. Buttons in the UI come from <c>allowedActions</c>.
/// Every response that carries an order also carries its <c>ETag</c>; a write must send it back as <c>If-Match</c> and is refused with 409 when someone else changed the order in the meantime.
/// </summary>
[Route(ApiRoutes.PurchaseOrders), RequiresModule(ModuleCodes.Purchasing)]
public class PurchaseOrdersController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    /// <summary>Purchase orders, newest first, in the warehouses the caller may read. Prices and totals are null for a caller without <c>cost.view</c>. With <c>status=Submitted</c> these are the orders waiting for approval.</summary>
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
    public Task<IActionResult> Update(Guid id, [FromBody] PurchaseOrderSaveRequest body) => SendOrder(new UpdatePurchaseOrderCommand(id, body, IfMatch));

    /// <summary>
    /// Sends a Draft on. Every line needs a price above 0. When an approval rule fires the order is Submitted and waits for the reviewer of its tier; when none fires it is Approved at once, with no approver. The total at cost is what the rules are tested against.
    /// </summary>
    [HttpPost(RouteSegments.Submit), Authorize(Policy = Permissions.PoManage)]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Submit(Guid id) => SendOrder(new SubmitPurchaseOrderCommand(id, IfMatch));

    /// <summary>Approves a Submitted order: the reviewer of its tier (a Supervisor for a standard order, the Owner for a large one, an Administrator either), never the person who submitted it unless self-approval is on.</summary>
    [HttpPost(RouteSegments.Approve), Authorize(Policy = AuthPolicies.DocumentReview)]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Approve(Guid id) => SendOrder(new ApprovePurchaseOrderCommand(id, IfMatch));

    /// <summary>Returns a Submitted order to Draft with a reason of at least 5 characters, as a review like approving.</summary>
    [HttpPost(RouteSegments.Reject), Authorize(Policy = AuthPolicies.DocumentReview)]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Reject(Guid id, [FromBody] RejectRequest body) => SendOrder(new RejectPurchaseOrderCommand(id, body, IfMatch));

    /// <summary>Takes the caller's own Submitted order back to Draft (the Edit button): same number, same lines, no reason. Repeating it on an order that is already an ordinary Draft answers 200 and changes nothing.</summary>
    [HttpPost(RouteSegments.Recall), Authorize(Policy = Permissions.PoManage)]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Recall(Guid id) => SendOrder(new RecallPurchaseOrderCommand(id, IfMatch));

    /// <summary>Cancels a Draft (by its creator), a Submitted order (by its submitter) or an Approved one (by a reviewer it is routed to). The reason is optional, except that cancelling an Approved order needs one of at least 5 characters.</summary>
    [HttpPost(RouteSegments.Cancel), Authorize(Policy = AuthPolicies.PurchaseOrderCancel)]
    [ProducesResponseType<PurchaseOrderDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Cancel(Guid id, [FromBody] CancelRequest body) => SendOrder(new CancelPurchaseOrderCommand(id, body, IfMatch));

    /// <summary>The concurrency token the client last saw; required on every write (a missing header is 428, see <see cref="IfMatchHeader"/>).</summary>
    private string? IfMatch => IfMatchHeader.Read(Request, required: true);

    private async Task<IActionResult> SendOrder(IRequest<PurchaseOrderDto> request)
    {
        var order = await Send(request);
        Response.Headers[HeaderNames.ETag] = $"\"{order.ConcurrencyToken}\"";
        return Ok(order);
    }
}

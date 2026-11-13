using Ims.Application;
using Ims.Application.Features.Sales;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ims.Api.Features.Sales;

/// <summary>
/// Price lists of the sales module (pilot slice PL3.11): the default list and at most one list per customer, each with the selling price of its items. While the module is off every signed-in caller gets the
/// 404 of an unknown route (<see cref="RequiresModuleAttribute"/>), before the permission is asked. Every endpoint but the lookup needs <c>pricelist.manage</c>: a selling price is not cost, so <c>cost.view</c> is not involved,
/// and a holder of <c>so.manage</c> alone does not see the lists. Every response that carries a list also carries its <c>ETag</c>; a change or a delete must send it back as <c>If-Match</c> and is refused with
/// 409 when someone else changed the list in the meantime. A list never changes a saved order.
/// </summary>
[Route(ApiRoutes.PriceLists), RequiresModule(ModuleCodes.Sales)]
public sealed class PriceListsController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    /// <summary>The price lists, the default first and then by name, one page at a time. Each carries its token so a list can be deleted without opening it.</summary>
    [HttpGet, Authorize(Policy = Permissions.PricelistManage)]
    [ProducesResponseType<PagedResult<PriceListListItem>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] PriceListFilter filter) => SendOk(new ListPriceListsQuery(filter));

    /// <summary>
    /// The price an order line is prefilled with (pilot slice PL3.12): the customer's own list item for the product and unit, else the default list's item for that product and unit, else nothing
    /// (<c>found</c> false: the person types the price). Per item, not per list. The unit is exact and nothing is converted; a unit with no item has no price. Asked by an order editor, so it needs
    /// <c>so.manage</c> and NOT <c>pricelist.manage</c> (the rest of this controller): a seller who cannot open the lists still sees the price they are offered. Read only; it audits nothing.
    /// </summary>
    [HttpGet(RouteSegments.Lookup), Authorize(Policy = Permissions.SoManage)]
    [ProducesResponseType<PriceLookupDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Lookup([FromQuery] PriceLookupFilter filter) => SendOk(new LookupPriceQuery(filter));

    [HttpGet(ApiRoutes.PublicId), Authorize(Policy = Permissions.PricelistManage)]
    [ProducesResponseType<PriceListDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Get(Guid id) => SendList(new GetPriceListQuery(id));

    /// <summary>
    /// Creates a list with its items. 409 when the name is taken, when it asks to be the default and another list is, or when its customer already has a list. The default list has no customer (400), a customer is an
    /// active partner of type Customer, and a product is priced once in each unit.
    /// </summary>
    [HttpPost, Authorize(Policy = Permissions.PricelistManage)]
    [ProducesResponseType<PriceListDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Create([FromBody] PriceListSaveRequest body) => SendList(new CreatePriceListCommand(body));

    /// <summary>Replaces the name, the default flag, the customer and all items of a list. Items the list already has keep their row; <c>If-Match</c> is required (428 without, 409 when stale).</summary>
    [HttpPut(ApiRoutes.PublicId), Authorize(Policy = Permissions.PricelistManage)]
    [ProducesResponseType<PriceListDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Update(Guid id, [FromBody] PriceListSaveRequest body) => SendList(new UpdatePriceListCommand(id, body, IfMatch));

    /// <summary>Gives the list to a customer, or takes it off its customer (<c>customerId</c> null), without sending the items again. <c>If-Match</c> is required. 409 when the customer already has a list; 400 for the default list.</summary>
    [HttpPut(RouteSegments.Customer), Authorize(Policy = Permissions.PricelistManage)]
    [ProducesResponseType<PriceListDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> SetCustomer(Guid id, [FromBody] PriceListCustomerRequest body) => SendList(new SetPriceListCustomerCommand(id, body, IfMatch));

    /// <summary>Deletes a list and its items. A saved order keeps the price it was saved with. <c>If-Match</c> is required.</summary>
    [HttpDelete(ApiRoutes.PublicId), Authorize(Policy = Permissions.PricelistManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await Send(new DeletePriceListCommand(id, IfMatch));
        return NoContent();
    }

    /// <summary>The concurrency token the client last saw; required on every change and on delete (a missing header is 428, see <see cref="IfMatchHeader"/>).</summary>
    private string? IfMatch => IfMatchHeader.Read(Request, required: true);

    private async Task<IActionResult> SendList(IRequest<PriceListDto> request)
    {
        var list = await Send(request);
        Response.Headers[HeaderNames.ETag] = $"\"{list.ConcurrencyToken}\"";
        return Ok(list);
    }
}

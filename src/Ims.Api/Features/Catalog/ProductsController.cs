using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Ims.Api.Features.Catalog;

[Route(ApiRoutes.Products)]
public class ProductsController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [HttpGet, Authorize(Policy = Permissions.MasterDataRead)]
    [ProducesResponseType<PagedResult<ProductDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] ProductFilter filter) => SendOk(new ListProductsQuery(filter));

    [HttpGet(ApiRoutes.PublicId), Authorize(Policy = Permissions.MasterDataRead)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Get(Guid id) => SendOk(new GetProductQuery(id));

    /// <summary>
    /// Barcode or SKU lookup for the line picker and scanning. Always 200: <c>found</c> says whether a product owns the code
    /// (<c>product</c> is null when it does not), because a scan that matches nothing is a normal outcome, not a failed request.
    /// </summary>
    [HttpGet(RouteSegments.Lookup), Authorize(Policy = Permissions.MasterDataRead)]
    [ProducesResponseType<ProductLookupResult>(StatusCodes.Status200OK)]
    public Task<IActionResult> Lookup([FromQuery] string code) => SendOk(new FindProductByCodeQuery(code));

    [HttpPost, Authorize(Policy = Permissions.MasterDataManage)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Create([FromBody] ProductSaveRequest body) => SendOk(new CreateProductCommand(body));

    [HttpPut(ApiRoutes.PublicId), Authorize(Policy = Permissions.MasterDataManage)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Update(Guid id, [FromBody] ProductSaveRequest body) => SendOk(new UpdateProductCommand(id, body));
}

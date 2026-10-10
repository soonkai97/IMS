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

    [HttpGet(ApiRoutes.Id), Authorize(Policy = Permissions.MasterDataRead)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Get(int id) => SendOk(new GetProductQuery(id));

    /// <summary>Barcode or SKU lookup for the line picker and scanning. 404 when nothing matches.</summary>
    [HttpGet(RouteSegments.Lookup), Authorize(Policy = Permissions.MasterDataRead)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Lookup([FromQuery] string code)
    {
        var product = await Send(new FindProductByCodeQuery(code));
        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost, Authorize(Policy = Permissions.MasterDataManage)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Create([FromBody] ProductSaveRequest body) => SendOk(new CreateProductCommand(body));

    [HttpPut(ApiRoutes.Id), Authorize(Policy = Permissions.MasterDataManage)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Update(int id, [FromBody] ProductSaveRequest body) => SendOk(new UpdateProductCommand(id, body));
}

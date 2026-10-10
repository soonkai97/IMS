using Microsoft.AspNetCore.Http.Timeouts;
using Ims.Application;
using Ims.Application.Features.Import;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Ims.Api.Features.Stock;

[Route(ApiRoutes.Import)]
public class ImportController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [ProducesFile(ContentTypes.Xlsx)]
    [HttpGet(RouteSegments.Templates), Authorize(Policy = Permissions.MasterDataManage)]
    public async Task<IActionResult> Template(string kind)
    {
        var f = await Send(new GetImportTemplateQuery(kind));
        return File(f.Content, f.ContentType, f.FileName);
    }

    /// <summary>Preview with <c>commit=false</c> (default); write with <c>commit=true</c>. Import is all-or-nothing: a commit with any invalid row writes nothing.</summary>
    [RequestTimeout(RequestTimeoutPolicies.Long)]
    [HttpPost(RouteSegments.Products), Authorize(Policy = Permissions.MasterDataManage)]
    [RequestSizeLimit(Limits.MaxImportFileBytes)]
    [ProducesResponseType<ImportResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ImportProducts(IFormFile file, [FromQuery] bool commit = false)
    {
        await using var stream = file.OpenReadStream();
        return Ok(await Send(new ImportProductsCommand(stream, commit)));
    }

    [RequestTimeout(RequestTimeoutPolicies.Long)]
    [HttpPost(RouteSegments.OpeningStock), Authorize(Policy = Permissions.MasterDataManage)]
    [RequestSizeLimit(Limits.MaxImportFileBytes)]
    [ProducesResponseType<ImportResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ImportOpeningStock(IFormFile file, [FromQuery] bool commit = false)
    {
        await using var stream = file.OpenReadStream();
        return Ok(await Send(new ImportOpeningStockCommand(stream, commit)));
    }
}

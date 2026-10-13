using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Ims.Api.Features.Catalog;

[Route(ApiRoutes.Categories)]
public class CategoriesController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [HttpGet, Authorize(Policy = Permissions.MasterDataRead)]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List() => SendOk(new ListCategoriesQuery());

    [HttpPost, Authorize(Policy = Permissions.MasterDataManage)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Create([FromBody] CategorySaveRequest body) => SendOk(new CreateCategoryCommand(body));

    [HttpPut(ApiRoutes.PublicId), Authorize(Policy = Permissions.MasterDataManage)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Update(Guid id, [FromBody] CategorySaveRequest body) => SendOk(new UpdateCategoryCommand(id, body));
}

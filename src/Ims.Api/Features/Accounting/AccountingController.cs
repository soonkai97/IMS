using Ims.Application;
using Ims.Application.Features.Accounting;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
namespace Ims.Api.Features.Accounting;

/// <summary>
/// The accounting module. While the module is off every signed-in caller gets the 404 of an unknown route (<see cref="RequiresModuleAttribute"/>), before the permission is asked.
/// </summary>
[Route(ApiRoutes.Accounting), RequiresModule(ModuleCodes.Accounting)]
public class AccountingController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    /// <summary>
    /// The accounting file of a period (<c>from</c> and <c>to</c>, both required) as one .xlsx in a fixed layout, at cost, optionally for one warehouse. A period with more rows than
    /// one file holds is a 400 that asks for a narrower range; the file is never cut short. A warehouse the caller may not read is a 404.
    /// </summary>
    [RequestTimeout(RequestTimeoutPolicies.Long)]
    [ProducesFile(ContentTypes.Xlsx)]
    [HttpGet(RouteSegments.Export), Authorize(Policy = Permissions.AccountingExport)]
    public async Task<IActionResult> Export([FromQuery] AccountingExportFilter filter)
    {
        var file = await Send(new ExportAccountingQuery(filter));
        Response.Headers[HeaderNames.CacheControl] = "private, no-store"; // cost and tax numbers: no shared cache keeps a copy
        return File(file.Content, file.ContentType, file.FileName);
    }
}

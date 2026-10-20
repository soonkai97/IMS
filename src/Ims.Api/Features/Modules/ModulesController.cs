using Ims.Application;
using Ims.Application.Features.Modules;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Ims.Api.Features.Modules;

/// <summary>
/// The modules of this installation and their switches (ADR modules-and-tracking-dimensions 4.2). Reading the list is open to any signed-in person, because the web shows or hides
/// parts of the screen by it. Turning a module on or off is for whoever holds <c>modules.manage</c>, needs a reason and is audited. This controller belongs to the core, so it
/// carries no <see cref="RequiresModuleAttribute"/>: a module that is off can always be turned on.
/// </summary>
[Route(ApiRoutes.Modules)]
public class ModulesController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ModuleDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List() => SendOk(new ListModulesQuery());

    [HttpPut(RouteSegments.ModuleCode), Authorize(Policy = Permissions.ModulesManage)]
    [ProducesResponseType<ModuleSwitchResult>(StatusCodes.Status200OK)]
    public Task<IActionResult> Switch(string code, [FromBody] ModuleSwitchRequest body) => SendOk(new SwitchModuleCommand(code, body));
}

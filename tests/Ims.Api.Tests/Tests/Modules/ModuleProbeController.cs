using Ims.Api;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ims.Api.Tests.Tests.Modules;

/// <summary>
/// Endpoints that exist only in the test host (<see cref="ModuleGateEndpointTests"/> adds this assembly as an application part), because the API has no module controller yet.
/// They behave exactly as a real module endpoint will: a gate attribute and a permission policy.
/// </summary>
[Route("api/probe")]
public class ModuleProbeController : ControllerBase
{
    /// <summary>Part of the purchasing module and guarded by a permission every system role holds, so only the gate and a forged token can tell callers apart.</summary>
    [HttpGet("gated"), RequiresModule(ModuleCodes.Purchasing), Authorize(Policy = Permissions.StockRead)]
    public IActionResult Gated() => Ok();

    /// <summary>Guarded by a module permission itself, held by some system roles only (po.manage): the claim decides once the gate lets the caller through.</summary>
    [HttpGet("manage"), RequiresModule(ModuleCodes.Purchasing), Authorize(Policy = Permissions.PoManage)]
    public IActionResult Manage() => Ok();

    /// <summary>Allows anonymous callers AND belongs to a module: the gate must still answer 404 while the module is off.</summary>
    [HttpGet("anonymous"), AllowAnonymous, RequiresModule(ModuleCodes.Purchasing)]
    public IActionResult Anonymous() => Ok();

    [HttpGet("unregistered"), RequiresModule("unregistered"), Authorize(Policy = Permissions.StockRead)]
    public IActionResult Unregistered() => Ok();

    [HttpGet("core"), Authorize(Policy = Permissions.StockRead)]
    public IActionResult Core() => Ok();
}

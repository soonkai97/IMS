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
    /// <summary>Part of the accounting module and guarded by a permission every system role holds, so only the gate and a forged token can tell callers apart.</summary>
    [HttpGet("accounting"), RequiresModule(ModuleCodes.Accounting), Authorize(Policy = Permissions.StockRead)]
    public IActionResult Accounting() => Ok();

    [HttpGet("unregistered"), RequiresModule("unregistered"), Authorize(Policy = Permissions.StockRead)]
    public IActionResult Unregistered() => Ok();

    [HttpGet("core"), Authorize(Policy = Permissions.StockRead)]
    public IActionResult Core() => Ok();
}

using Ims.Application;
using Ims.Application.Features.Settings;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Ims.Api.Features.Settings;

[Route(ApiRoutes.ApprovalRules)]
public class ApprovalRulesController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [HttpGet, Authorize(Policy = Permissions.MasterDataRead)]
    [ProducesResponseType<IReadOnlyList<ApprovalRuleDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List() => SendOk(new ListApprovalRulesQuery());

    [HttpPost, Authorize(Policy = Permissions.SettingsManage)]
    [ProducesResponseType<ApprovalRuleDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Create([FromBody] ApprovalRuleSaveRequest body) => SendOk(new CreateApprovalRuleCommand(body));

    [HttpPut(ApiRoutes.PublicId), Authorize(Policy = Permissions.SettingsManage)]
    [ProducesResponseType<ApprovalRuleDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Update(Guid id, [FromBody] ApprovalRuleSaveRequest body) => SendOk(new UpdateApprovalRuleCommand(id, body));
}

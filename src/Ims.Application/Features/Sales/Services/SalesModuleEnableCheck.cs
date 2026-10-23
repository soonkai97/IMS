using Ims.Application.Features.Documents;
using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>
/// Sales is turned on only while at least one approval rule for sales orders is active. With none, <c>ApprovalPolicy.RequiresApproval</c> is false and every priced order is released
/// with no approver. Until the data migration of PL3.4 writes the standard pair, no rule exists, so the switch-on is refused until an Administrator adds one in Settings.
/// </summary>
public class SalesModuleEnableCheck(IReadRepository<ApprovalRule> rules) : IModuleEnableCheck, IAppService
{
    public string ModuleCode => ModuleCodes.Sales;

    public async Task<string?> RefusalAsync(CancellationToken ct) =>
        await rules.AnyAsync(new ActiveRulesForTypeSpec(DocumentType.SalesOrder), ct) ? null : Messages.SalesOrderRuleNeededToTurnOn;
}

using Ims.Application.Features.Documents;
using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>
/// Purchasing is turned on only while at least one approval rule for purchase orders is active. With none, <c>ApprovalPolicy.RequiresApproval</c> is false and every priced order is released
/// with no approver. The Seeder writes the standard pair, but only where it runs; this closes the case where it did not.
/// </summary>
public class PurchasingModuleEnableCheck(IReadRepository<ApprovalRule> rules) : IModuleEnableCheck, IAppService
{
    public string ModuleCode => ModuleCodes.Purchasing;

    public async Task<string?> RefusalAsync(CancellationToken ct) =>
        await rules.AnyAsync(new ActiveRulesForTypeSpec(DocumentType.PurchaseOrder), ct) ? null : Messages.PurchaseOrderRuleNeededToTurnOn;
}

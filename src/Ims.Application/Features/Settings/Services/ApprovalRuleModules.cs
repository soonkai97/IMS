using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Settings;

/// <summary>
/// Which approval-rule subjects the installation has. A rule for purchase orders belongs to the purchasing module: it can be written only while the module is on (a 400 naming <c>docType</c>
/// otherwise), and while the module is off such rules are kept but neither listed nor changed (a request for one is a 404), like any other data of an off module.
/// </summary>
public static class ApprovalRuleModules
{
    /// <exception cref="ValidationException">The type belongs to a module that is off.</exception>
    public static async Task EnsureTypeAvailableAsync(this IModuleSwitches modules, DocumentType type, CancellationToken ct)
    {
        if (type == DocumentType.PurchaseOrder) await modules.AcceptAsync(ModuleCodes.Purchasing, [("docType", type.ToString())], ct);
    }

    /// <summary>The subjects whose rules are hidden now: a purchase-order rule while purchasing is off.</summary>
    public static async Task<IReadOnlyList<DocumentType>> HiddenTypesAsync(this IModuleSwitches modules, CancellationToken ct) =>
        await modules.IsEnabledAsync(ModuleCodes.Purchasing, ct) ? [] : [DocumentType.PurchaseOrder];
}

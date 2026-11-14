using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Settings;

/// <summary>
/// What the module switches say about approval rules (the facts of who owns which subject are <see cref="ApprovalRuleModules"/>, in the Domain): a rule for an order type can be written only
/// while its module is on (a 400 naming <c>docType</c> otherwise), and while the module is off such rules are kept but neither listed nor changed (a request for one is a 404).
/// </summary>
public static class ApprovalRuleModuleSwitches
{
    /// <exception cref="ValidationException">The type belongs to a module that is off.</exception>
    public static async Task EnsureTypeAvailableAsync(this IModuleSwitches modules, DocumentType type, CancellationToken ct)
    {
        if (ApprovalRuleModules.ModuleOf(type) is { } module) await modules.AcceptAsync(module, [("docType", type.ToString())], ct);
    }

    /// <summary>The subjects whose rules are hidden now: the rules of an order type whose module is off.</summary>
    public static async Task<IReadOnlyList<DocumentType>> HiddenTypesAsync(this IModuleSwitches modules, CancellationToken ct)
    {
        var hidden = new List<DocumentType>();
        foreach (var type in new[] { DocumentType.PurchaseOrder, DocumentType.SalesOrder })
            if (!await modules.IsEnabledAsync(ApprovalRuleModules.ModuleOf(type)!, ct)) hidden.Add(type);
        return hidden;
    }
}

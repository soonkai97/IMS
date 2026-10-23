using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Settings;

/// <summary>
/// Which approval-rule subjects the installation has. A rule for an order type belongs to the module that owns the order (purchase orders to purchasing, sales orders to sales): it can be
/// written only while the module is on (a 400 naming <c>docType</c> otherwise), and while the module is off such rules are kept but neither listed nor changed (a request for one is a 404),
/// like any other data of an off module.
/// </summary>
public static class ApprovalRuleModules
{
    /// <summary>The module that owns the document type, or null when the type is a stock document, which no module owns.</summary>
    public static string? ModuleOf(DocumentType type) => type switch
    {
        DocumentType.PurchaseOrder => ModuleCodes.Purchasing,
        DocumentType.SalesOrder => ModuleCodes.Sales,
        _ => null
    };

    /// <summary>The subject types an order type is made of: a rule of one of them cannot be moved to another type, and no other rule can become one.</summary>
    public static bool IsOrderType(DocumentType type) => ModuleOf(type) is not null;

    /// <exception cref="ValidationException">The type belongs to a module that is off.</exception>
    public static async Task EnsureTypeAvailableAsync(this IModuleSwitches modules, DocumentType type, CancellationToken ct)
    {
        if (ModuleOf(type) is { } module) await modules.AcceptAsync(module, [("docType", type.ToString())], ct);
    }

    /// <summary>The subjects whose rules are hidden now: the rules of an order type whose module is off.</summary>
    public static async Task<IReadOnlyList<DocumentType>> HiddenTypesAsync(this IModuleSwitches modules, CancellationToken ct)
    {
        var hidden = new List<DocumentType>();
        foreach (var type in new[] { DocumentType.PurchaseOrder, DocumentType.SalesOrder })
            if (!await modules.IsEnabledAsync(ModuleOf(type)!, ct)) hidden.Add(type);
        return hidden;
    }
}

namespace Ims.Domain;

/// <summary>
/// Which approval-rule subjects belong to a module. A rule for an order type belongs to the module that owns the order (purchase orders to purchasing, sales orders to sales): it can be
/// written only while the module is on, and while the module is off such rules are kept but neither listed nor changed, like any other data of an off module. Application asks the switches
/// (<c>ApprovalRuleModuleSwitches</c>); the fact of who owns what is here.
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
}

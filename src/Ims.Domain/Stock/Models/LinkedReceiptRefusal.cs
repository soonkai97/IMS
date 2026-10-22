namespace Ims.Domain;

/// <summary>Why the API refuses an action on a receipt that was created from a purchase order, before it looks at the order itself (<see cref="LinkedReceiptPolicy"/>).</summary>
public enum LinkedReceiptRefusal
{
    None = 0,

    /// <summary>The caller may not read purchase orders (403).</summary>
    NeedsPoRead,

    /// <summary>The Purchasing module is off, so the order behind the receipt is not reachable (409).</summary>
    ModuleOff
}

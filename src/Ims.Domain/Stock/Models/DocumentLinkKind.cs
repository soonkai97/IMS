namespace Ims.Domain;

/// <summary>What a stock document is linked to, if anything. A document carries at most ONE link (plan D8): a receipt created from a purchase order, or a delivery created from a sales order.</summary>
public enum DocumentLinkKind { None = 0, PurchaseOrder = 1, SalesOrder = 2 }

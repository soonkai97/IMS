namespace Ims.Domain;

/// <summary>How a purchase order was closed: every line was received, or a person closed it short with a reason.</summary>
public enum PurchaseOrderCloseKind { Received = 1, Short = 2 }

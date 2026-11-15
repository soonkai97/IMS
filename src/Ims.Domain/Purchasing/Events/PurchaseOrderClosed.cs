namespace Ims.Domain;

/// <summary>An Approved order was closed: as received (the posted receipt that brought in the last quantity is named, no reason) or short (a reason, no receipt). Carries what the audit rows of both closes print.</summary>
public sealed record PurchaseOrderClosed(string No, PurchaseOrderStatus StatusBefore, PurchaseOrderStatus StatusAfter, PurchaseOrderCloseKind Kind, string? Reason, string? Receipt) : IDomainEvent;

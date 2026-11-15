namespace Ims.Domain;

/// <summary>A Submitted order was approved. Carries what the audit row of an approval prints.</summary>
public sealed record PurchaseOrderApproved(string No, PurchaseOrderStatus StatusBefore, PurchaseOrderStatus StatusAfter, bool IsLarge) : IDomainEvent;

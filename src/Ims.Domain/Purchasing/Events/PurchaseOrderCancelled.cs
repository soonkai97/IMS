namespace Ims.Domain;

/// <summary>A Draft, Submitted or Approved order was cancelled. The reason (trimmed) is null when none was given, which only a Draft or Submitted order may do. Carries what the audit row of a cancellation prints.</summary>
public sealed record PurchaseOrderCancelled(string No, PurchaseOrderStatus StatusBefore, PurchaseOrderStatus StatusAfter, string? Reason) : IDomainEvent;
